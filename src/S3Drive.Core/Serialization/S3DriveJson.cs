namespace S3Drive.Core.Serialization
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Shared System.Text.Json options for all S3Drive fixed contracts (configuration, status,
    /// and commands). Enums serialize as strings, output is indented, null values are omitted,
    /// and property names are matched case-insensitively.
    /// </summary>
    public static class S3DriveJson
    {
        private static readonly JsonSerializerOptions _Options = BuildOptions();

        /// <summary>
        /// The shared serializer options. Never null.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get { return _Options; }
        }

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
