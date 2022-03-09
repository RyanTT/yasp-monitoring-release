using MediatR;

namespace YASP.Server.Application.Clustering.Communication
{
    /// <summary>
    /// Base class for requests that also implements <see cref="IRequest{TResponse}"/> for compatibility with MediatR.
    /// </summary>
    /// <typeparam name="TResponse"></typeparam>
    public abstract class RequestBase<TResponse> : IRequest<TResponse>, IRequestBase
    {

    }
}
