using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TerminalChess;

public class UciConnector : IDisposable
{
    private const string UCI_COMMAND = "uci";
    private const string UCI_OK_RESPONSE = "uciok";
    private const string IS_READY_COMMAND = "isready";
    private const string READY_OK_RESPONSE = "readyok";
    private const string NEW_GAME_COMMAND = "ucinewgame";
    private const string POSITION_COMMAND = "position startpos";
    // A força é controlada por UCI_Elo/Skill Level, o tempo por lance só limita a busca
    private const string GO_COMMAND = "go movetime 1000";
    private const string STOP_COMMAND = "stop";
    private const string BEST_MOVE_RESPONSE = "bestmove";
    private const string QUIT_COMMAND = "quit";
    private const string SET_OPTION_COMMAND_PATTERN = "setoption name {0} value {1}";

    private const string THREADS_OPTION = "Threads";
    private const string SKILL_LEVEL_OPTION = "Skill Level";
    private const string SKILL_OPTION = "Skill";

    // Os valores de GameLevel seguem a escala de 0 a 20 do Skill Level do Stockfish
    private const int GAME_LEVEL_MAX_SKILL = 20;
    private const string LIMIT_STRENGTH_OPTION = "UCI_LimitStrength";
    private const string ELO_OPTION = "UCI_Elo";

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(2);

    private readonly MatchOptions _options;

    // Opções anunciadas pela engine no handshake, nem toda engine suporta Threads, Skill Level ou UCI_Elo
    private readonly Dictionary<string, UciOption> _supportedOptions = new(StringComparer.OrdinalIgnoreCase);

    // Limites só existem para opções do tipo spin
    private readonly record struct UciOption(int? Min, int? Max)
    {
        public bool Contains(int value) => (Min == null || value >= Min) && (Max == null || value <= Max);

        public int Clamp(int value) => Math.Min(Math.Max(value, Min ?? int.MinValue), Max ?? int.MaxValue);
    }

    private Process? _process;

    // Linhas lidas da saída da engine em background, null indica que a saída foi fechada
    private BlockingCollection<string?>? _output;

    public UciConnector(MatchOptions options)
    {
        _options = options;
    }

    public void Connect()
    {
        if (_process != null)
            return;

        try
        {
            var output = new BlockingCollection<string?>();
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                // Algumas engines (ex.: lc0) escrevem banners e logs no stderr, que apareceriam no meio do tabuleiro
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Sem BOM: algumas engines leem o BOM como parte do primeiro comando
                StandardInputEncoding = new UTF8Encoding(false),
                FileName = _options.EnginePath ?? "stockfish.exe"
            };
            process.OutputDataReceived += (_, e) => output.Add(e.Data);
            // O stderr é descartado, mas precisa ser consumido: com o pipe cheio a engine travaria ao escrever nele
            process.ErrorDataReceived += static (_, _) => { };

            if (!process.Start())
                throw new ApplicationException("Failed to start process");

            _process = process;
            _output = output;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _supportedOptions.Clear();
            WriteCommand(UCI_COMMAND);
            if (WaitForResponse(UCI_OK_RESPONSE, ResponseTimeout, RegisterOption) == null)
                throw new UCIConnectionException("The engine did not answer the UCI handshake, verify if it is an UCI engine");

            SetOption(THREADS_OPTION, Math.Max(_options.EngineThreads, 1));
            ConfigureStrength();

            WaitReady();
        }
        catch (Exception ex)
        {
            Disconnect();

            throw new UCIConnectionException("Could not connect to UCI engine, verify inner exception for more details",
                ex);
        }
    }

    public void Disconnect()
    {
        var process = _process;
        if (process == null)
            return;

        _process = null;
        _output = null;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Write(QUIT_COMMAND + "\n");
                process.StandardInput.Flush();

                if (!process.WaitForExit(QuitTimeout))
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            // A engine já foi encerrada, não há o que fazer
        }
        finally
        {
            process.Dispose();
        }
    }

    public void StartGame()
    {
        WriteCommand(NEW_GAME_COMMAND);
        WaitReady();
    }

    /// <summary>
    /// Pede o próximo lance para a engine a partir da posição inicial e dos lances já jogados.
    /// Enviar o histórico (e não apenas o FEN) permite que a engine considere repetições de posição
    /// </summary>
    /// <returns>O lance em notação UCI, ou null se a engine não tiver um lance para jogar</returns>
    public string? GetNextMove(IReadOnlyCollection<string> moves)
    {
        // Garante que a engine continua respondendo antes de iniciar a busca
        WaitReady();

        var position = moves.Count == 0 ? POSITION_COMMAND : $"{POSITION_COMMAND} moves {string.Join(' ', moves)}";
        WriteCommand(position);
        WriteCommand(GO_COMMAND);

        var response = WaitForResponse(BEST_MOVE_RESPONSE, SearchTimeout);
        if (response == null)
        {
            // A busca demorou demais, pede para a engine parar e responder com o melhor lance até agora
            WriteCommand(STOP_COMMAND);
            response = WaitForResponse(BEST_MOVE_RESPONSE, ResponseTimeout) ??
                       throw new UCIConnectionException("The engine did not answer the move request in time");
        }

        return ParseBestMove(response);
    }

    /// <summary>
    /// Extrai o lance da resposta "bestmove &lt;lance&gt; [ponder &lt;lance&gt;]".
    /// Engines indicam ausência de lance de formas diferentes: "(none)", "0000", "a1a1", ou omitindo o lance
    /// </summary>
    private static string? ParseBestMove(string response)
    {
        var tokens = response.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
            return null;

        var move = tokens[1].ToLowerInvariant();
        if (!RegexUtils.MoveRegex().IsMatch(move))
            return null;

        // Origem igual ao destino não é um lance real
        if (move.AsSpan(0, 2).SequenceEqual(move.AsSpan(2, 2)))
            return null;

        return move;
    }

    /// <summary>
    /// Define a força da engine para o nível escolhido.
    /// UCI_Elo é a opção padrão do protocolo, mas cada engine tem um Elo mínimo (1320 no Stockfish).
    /// Se o Elo desejado estiver abaixo dele, uma opção de habilidade específica da engine consegue enfraquecer mais:
    /// Skill Level (Stockfish) ou Skill (Dragon/Komodo)
    /// </summary>
    private void ConfigureStrength()
    {
        var elo = _options.EngineElo;
        var hasElo = _supportedOptions.TryGetValue(ELO_OPTION, out var eloOption) &&
                     _supportedOptions.ContainsKey(LIMIT_STRENGTH_OPTION);

        string? skillOptionName = null;
        if (_supportedOptions.ContainsKey(SKILL_LEVEL_OPTION))
            skillOptionName = SKILL_LEVEL_OPTION;
        else if (_supportedOptions.ContainsKey(SKILL_OPTION))
            skillOptionName = SKILL_OPTION;

        if (hasElo && (eloOption.Contains(elo) || skillOptionName == null))
        {
            SetOption(LIMIT_STRENGTH_OPTION, "true");
            SetOption(ELO_OPTION, elo);
            return;
        }

        if (skillOptionName != null)
            SetOption(skillOptionName, ScaleSkill(_supportedOptions[skillOptionName]));
    }

    /// <summary>
    /// Converte o nível (escala de 0 a 20) para o intervalo anunciado pela engine, ex.: 0 a 25 no Skill do Dragon
    /// </summary>
    private int ScaleSkill(UciOption option)
    {
        var level = (int)_options.Level;
        if (option.Min is not { } min || option.Max is not { } max)
            return level;

        return (int)Math.Round(min + (max - min) * level / (double)GAME_LEVEL_MAX_SKILL);
    }

    private void RegisterOption(string line)
    {
        // Formato: option name <nome, pode conter espaços> type <tipo> [default <valor>] [min <valor>] [max <valor>] ...
        const string namePrefix = "option name ";
        if (!line.StartsWith(namePrefix, StringComparison.Ordinal))
            return;

        var typeIndex = line.IndexOf(" type ", namePrefix.Length, StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            _supportedOptions[line[namePrefix.Length..].Trim()] = new UciOption(null, null);
            return;
        }

        var name = line[namePrefix.Length..typeIndex].Trim();
        var tokens = line[typeIndex..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        _supportedOptions[name] = new UciOption(GetIntValue(tokens, "min"), GetIntValue(tokens, "max"));
    }

    private static int? GetIntValue(string[] tokens, string key)
    {
        var index = Array.IndexOf(tokens, key);
        if (index < 0 || index + 1 >= tokens.Length)
            return null;

        return int.TryParse(tokens[index + 1], out var value) ? value : null;
    }

    private void SetOption(string name, int value)
    {
        // Valores fora do intervalo anunciado são rejeitados ou ignorados por algumas engines
        if (_supportedOptions.TryGetValue(name, out var option))
            SetOption(name, option.Clamp(value).ToString());
    }

    private void SetOption(string name, string value)
    {
        if (!_supportedOptions.ContainsKey(name))
            return;

        WriteCommand(string.Format(SET_OPTION_COMMAND_PATTERN, name, value));
    }

    private void WaitReady()
    {
        WriteCommand(IS_READY_COMMAND);
        if (WaitForResponse(READY_OK_RESPONSE, ResponseTimeout) == null)
            throw new UCIConnectionException("The engine is not responding");
    }

    private void WriteCommand(string cmd)
    {
        VerifyConnection();

        try
        {
            _process.StandardInput.Write(cmd + "\n");
            _process.StandardInput.Flush();
        }
        catch (IOException ex)
        {
            throw new UCIConnectionException("UCI connection was closed", ex);
        }
    }

    /// <summary>
    /// Lê a saída da engine até encontrar uma linha iniciada pela resposta esperada
    /// </summary>
    /// <returns>A linha encontrada, ou null se o tempo limite for atingido</returns>
    private string? WaitForResponse(string response, TimeSpan timeout, Action<string>? onLine = null)
    {
        VerifyConnection();

        var output = _output;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero || !output.TryTake(out var line, remaining))
                return null;

            if (line == null)
            {
                // Mantém o marcador de fim para as próximas leituras
                output.Add(null);
                throw new UCIConnectionException("UCI connection was closed");
            }

            onLine?.Invoke(line);

            var trimmed = line.AsSpan().Trim();
            if (trimmed.StartsWith(response, StringComparison.Ordinal) &&
                (trimmed.Length == response.Length || char.IsWhiteSpace(trimmed[response.Length])))
                return line;
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    [MemberNotNull(nameof(_process), nameof(_output))]
    private void VerifyConnection()
    {
        if (_process == null || _output == null)
            throw new UCIConnectionException("UCI is not yet connected");

        bool hasExited;
        try
        {
            hasExited = _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            hasExited = true;
        }

        if (hasExited)
            throw new UCIConnectionException("UCI connection was closed");
    }
}
