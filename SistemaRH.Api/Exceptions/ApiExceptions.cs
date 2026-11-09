namespace SistemaRH.Api.Exceptions;

/// <summary>
/// Classe-base pros erros "esperados" de regra de negócio (ex.: registro não
/// encontrado, e-mail já cadastrado). A diferença pra uma exceção comum é que
/// o ExceptionMiddleware sabe transformar essas aqui numa resposta HTTP com o
/// StatusCode certo — qualquer outra exceção vira um 500 genérico.
/// </summary>
public abstract class ApiException : Exception
{
    public int StatusCode { get; }

    protected ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Usado quando um registro (usuário, ausência, etc.) não existe. Vira 404.</summary>
public class NotFoundException : ApiException
{
    public NotFoundException(string message) : base(StatusCodes.Status404NotFound, message)
    {
    }
}

/// <summary>Usado quando os dados enviados são válidos no formato, mas violam uma regra. Vira 400.</summary>
public class BusinessRuleException : ApiException
{
    public BusinessRuleException(string message) : base(StatusCodes.Status400BadRequest, message)
    {
    }
}

/// <summary>Usado quando o usuário autenticado tenta mexer em algo que não é dele. Vira 403.</summary>
public class ForbiddenException : ApiException
{
    public ForbiddenException(string message) : base(StatusCodes.Status403Forbidden, message)
    {
    }
}

/// <summary>Usado em conflitos, como tentar cadastrar um e-mail que já existe. Vira 409.</summary>
public class ConflictException : ApiException
{
    public ConflictException(string message) : base(StatusCodes.Status409Conflict, message)
    {
    }
}
