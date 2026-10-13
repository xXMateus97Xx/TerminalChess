namespace TerminalChess;

public class UCIConnectionException : Exception
{
    public UCIConnectionException() { }

    public UCIConnectionException(string message) : base(message) { }
        
    public UCIConnectionException(string message, System.Exception inner) : base(message, inner) { }
}