using MediatR;

using YASP.Server.Application.Clustering.Communication;

namespace YASP.Server.Application.Configuration.Messages
{
    /// <summary>
    /// Network request to propose a new configuration to the cluster. May only be sent to the leader.
    /// </summary>
    public class ProposeConfigurationRequest : RequestBase<Unit>
    {
        /// <summary>
        /// The new configuration to be proposed.
        /// </summary>
        public AppConfiguration ProposedConfiguration { get; set; }

        /// <summary>
        /// Force the proposed configuration to be applied if the revision matches the currently active configuration. Usually this would be set to <see cref="true"/> by the leader.
        /// </summary>
        public bool ForceOnEqualRevision { get; set; }

        public class Handler : IRequestHandler<ProposeConfigurationRequest>
        {
            private readonly AppConfigurationFileHandler _volatileConfigurationHandler;

            public Handler(AppConfigurationFileHandler volatileConfigurationHandler)
            {
                _volatileConfigurationHandler = volatileConfigurationHandler;
            }

            public async Task<Unit> Handle(ProposeConfigurationRequest request, CancellationToken cancellationToken)
            {
                await _volatileConfigurationHandler.HandleProposedConfigurationAsync(request.ProposedConfiguration, request.ForceOnEqualRevision);

                return Unit.Value;
            }
        }
    }
}
