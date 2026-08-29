namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using S3Drive.Core.Configuration;

    /// <summary>
    /// Storage integration configuration built from command-line arguments or environment
    /// variables. When no endpoint and credentials are supplied, integration tests are skipped.
    /// </summary>
    public sealed class StorageTestConfig
    {
        /// <summary>
        /// The drive profile describing the endpoint under test.
        /// </summary>
        public DriveProfile Profile { get; private set; } = new DriveProfile();

        /// <summary>
        /// The plaintext secret key for the endpoint.
        /// </summary>
        public string Secret { get; private set; } = string.Empty;

        /// <summary>
        /// Whether integration tests should run.
        /// </summary>
        public bool Enabled { get; private set; }

        /// <summary>
        /// Builds a configuration from CLI arguments (for example --endpoint, --access-key,
        /// --secret-key, --bucket, --region, --provider, --ssl, --path-style) or the matching
        /// S3DRIVE_TEST_* environment variables.
        /// </summary>
        /// <param name="args">The process arguments.</param>
        /// <returns>The configuration; <see cref="Enabled"/> is false when insufficient values are supplied.</returns>
        public static StorageTestConfig FromArgs(string[] args)
        {
            Dictionary<string, string> map = ParseArgs(args);

            string? endpoint = Lookup(map, "endpoint", "S3DRIVE_TEST_ENDPOINT");
            string? accessKey = Lookup(map, "access-key", "S3DRIVE_TEST_ACCESS_KEY");
            string? secret = Lookup(map, "secret-key", "S3DRIVE_TEST_SECRET_KEY");
            string? bucket = Lookup(map, "bucket", "S3DRIVE_TEST_BUCKET");
            string region = Lookup(map, "region", "S3DRIVE_TEST_REGION") ?? "us-east-1";
            string? provider = Lookup(map, "provider", "S3DRIVE_TEST_PROVIDER");

            bool hasEndpoint = !string.IsNullOrEmpty(endpoint);
            bool ssl = ParseBool(Lookup(map, "ssl", "S3DRIVE_TEST_SSL"), !hasEndpoint);
            bool pathStyle = ParseBool(Lookup(map, "path-style", "S3DRIVE_TEST_PATH_STYLE"), hasEndpoint);

            StorageTestConfig config = new StorageTestConfig();
            if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(bucket))
            {
                return config;
            }

            bool isCompatible = hasEndpoint || string.Equals(provider, "s3compatible", StringComparison.OrdinalIgnoreCase);

            config.Profile = new DriveProfile
            {
                Id = "drv_test",
                Name = "Integration",
                Provider = isCompatible ? S3ProviderEnum.S3Compatible : S3ProviderEnum.AwsS3,
                ServiceUrl = endpoint,
                UseSsl = ssl,
                Region = region,
                Bucket = bucket,
                AccessKey = accessKey,
                UsePathStyle = pathStyle,
                DriveLetter = "Z:"
            };
            config.Secret = secret;
            config.Enabled = true;
            return config;
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;

                string key = arg.Substring(2);
                int equals = key.IndexOf('=');
                if (equals >= 0)
                {
                    map[key.Substring(0, equals)] = key.Substring(equals + 1);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    map[key] = args[i + 1];
                    i++;
                }
                else
                {
                    map[key] = "true";
                }
            }

            return map;
        }

        private static string? Lookup(Dictionary<string, string> map, string argName, string envName)
        {
            if (map.TryGetValue(argName, out string? value) && !string.IsNullOrEmpty(value)) return value;
            string? env = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrEmpty(env) ? null : env;
        }

        private static bool ParseBool(string? value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return bool.TryParse(value.Trim(), out bool parsed) ? parsed : fallback;
        }
    }
}
