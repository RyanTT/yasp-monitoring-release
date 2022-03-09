namespace YASP.Server.Application.Monitoring.Objects
{
    [Serializable]
    public class MonitorConfiguration
    {
        /// <summary>
        /// ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Display name of the monitor.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Amount of nodes this monitor should be monitored on.
        /// </summary>
        public int CheckWith { get; set; }

        /// <summary>
        /// CRON interval in which the checks should be run.
        /// </summary>
        public string Interval { get; set; }

        /// <summary>
        /// Check timeout in seconds.
        /// </summary>
        public double Timeout { get; set; } = 10;

        /// <summary>
        /// Availability threshold (>=) above which the monitor is considered fully available.
        /// </summary>
        public double AvailableThreshold { get; set; }

        /// <summary>
        /// Availability threshold (>=) above which the monitor is considered partially available.
        /// </summary>
        public double PartialThreshold { get; set; }

        /// <summary>
        /// HTTP specific settings.
        /// </summary>
        public HttpCheckConfiguration Http { get; set; }

        /// <summary>
        /// TCP specific settings.
        /// </summary>
        public TcpCheckConfiguration Tcp { get; set; }

        public class HttpCheckConfiguration
        {
            /// <summary>
            /// URL to check.
            /// </summary>
            public string Url { get; set; }

            /// <summary>
            /// Keyword to check for inside the HTML.
            /// </summary>
            public string Keyword { get; set; }

            /// <summary>
            /// HTTP method with which the request should be sent.
            /// </summary>
            public string Method { get; set; }

            /// <summary>
            /// List of status codes that are supposed to result in a valid check. May be a single number per entry or a range (100-200).
            /// </summary>
            public string[] StatusCodes { get; set; }

            public bool IsValidStatusCode(int statusCode)
            {
                foreach (var statusCodeRange in StatusCodes)
                {
                    try
                    {
                        if (statusCodeRange.Contains("-"))
                        {
                            var statusCodes = statusCodeRange.Split('-');

                            if (statusCodes.Length != 2) continue;


                            var lowerInclusive = int.Parse(statusCodes[0]);
                            var upperInclusive = int.Parse(statusCodes[1]);

                            if (lowerInclusive <= statusCode && statusCode <= upperInclusive) return true;

                        }
                        else
                        {
                            var expectedCode = int.Parse(statusCodeRange);

                            if (expectedCode == statusCode) return true;
                        }
                    }
                    catch
                    {
                    }
                }

                return false;
            }
        }

        public class TcpCheckConfiguration
        {
            /// <summary>
            /// Host to connect to.
            /// </summary>
            public string Host { get; set; }

            /// <summary>
            /// Port to connect to.
            /// </summary>
            public int Port { get; set; }
        }
    }
}
