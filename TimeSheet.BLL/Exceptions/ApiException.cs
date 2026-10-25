namespace TimeSheet.BLL.Exceptions
{
    public abstract class ApiException : Exception
    {
        public int StatusCode { get; }
        protected ApiException(string message, int statusCode) : base(message)
            => StatusCode = statusCode;
    }

    public sealed class BadRequestException : ApiException
    {
        public BadRequestException(string message) : base(message, 400) { }
    }

    public sealed class NotFoundException : ApiException
    {
        public NotFoundException(string message) : base(message, 404) { }
    }

    public sealed class ForbiddenException : ApiException
    {
        public ForbiddenException(string message) : base(message, 403) { }
    }

    public sealed class UnauthorizedException : ApiException
    {
        public UnauthorizedException(string message) : base(message, 401) { }
    }
}
