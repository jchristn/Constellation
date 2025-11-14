namespace Constellation.Controller
{
    using System;
    using System.Collections.Generic;
    using Constellation.Core;
    using SyslogLogging;

    /// <summary>
    /// Admin settings.
    /// </summary>
    public class AdminSettings
    {
        /// <summary>
        /// API key header.
        /// </summary>
        public string ApiKeyHeader
        {
            get => _ApiKeyHeader;
            set => _ApiKeyHeader = (!String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(ApiKeyHeader)));
        }

        /// <summary>
        /// API key.
        /// </summary>
        public List<string> ApiKeys
        {
            get => _ApiKeys;
            set => _ApiKeys = (value != null && value.Count > 0 ? value : throw new ArgumentException("At least one API key must be specified."));
        }

        private string _ApiKeyHeader = "x-api-key";
        private List<string> _ApiKeys = new List<string> 
        {
            "constellationadmin"
        };

        /// <summary>
        /// Admin settings.
        /// </summary>
        public AdminSettings()
        {

        }
    }
}