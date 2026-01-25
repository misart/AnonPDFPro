using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

// Suppress spell-check warning for project name 'AnonPDF'
#pragma warning disable SPELL
namespace AnonPDF
{
    internal sealed class AppConfig
    {
        private AppConfig(
            string licenseFile,
            string publicKeyFile,
            string serverBaseUrl,
            string defaultTheme)
        {
            LicenseFile = licenseFile;
            PublicKeyFile = publicKeyFile;
            ServerBaseUrl = serverBaseUrl;
            DefaultTheme = defaultTheme;
        }

        internal string LicenseFile { get; }
        internal string PublicKeyFile { get; }
        internal string ServerBaseUrl { get; }
        internal string DefaultTheme { get; }

        internal static AppConfig Load(string baseDir)
        {
            string configPath = Path.Combine(baseDir, "config.json");
            var defaults = new AppConfig(
                licenseFile: "license.json",
                publicKeyFile: "license_public.xml",
                serverBaseUrl: "https://misart.pl/anonpdfpro",
                defaultTheme: string.Empty);

            if (!File.Exists(configPath))
            {
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var root = JObject.Parse(json);
                return new AppConfig(
                    licenseFile: (string)root["licenseFile"] ?? defaults.LicenseFile,
                    publicKeyFile: (string)root["publicKeyFile"] ?? defaults.PublicKeyFile,
                    serverBaseUrl: (string)root["serverBaseUrl"] ?? defaults.ServerBaseUrl,
                    defaultTheme: (string)root["defaultTheme"] ?? defaults.DefaultTheme);
            }
            catch
            {
                return defaults;
            }
        }

        internal string ResolveLicensePath(string baseDir)
        {
            return ResolvePath(baseDir, LicenseFile);
        }

        internal string ResolvePublicKeyPath(string baseDir)
        {
            return ResolvePath(baseDir, PublicKeyFile);
        }

        private static string ResolvePath(string baseDir, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Path.Combine(baseDir, "license.json");
            }

            return Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
        }
    }

    internal sealed class LicenseInfo
    {
        private LicenseInfo(
            LicensePayload payload,
            bool isSignatureValid,
            string error)
        {
            Payload = payload;
            IsSignatureValid = isSignatureValid;
            Error = error;

            if (!IsSignatureValid || Payload == null)
            {
                IsDemoExpired = true;
                return;
            }

            IsDemo = string.Equals(Payload.Edition, "demo", StringComparison.OrdinalIgnoreCase);
            if (!IsDemo)
            {
                IsDemoExpired = false;
                return;
            }

            var demoUntil = ParseDate(Payload.DemoUntil);
            if (!demoUntil.HasValue)
            {
                IsDemoExpired = true;
                return;
            }

            IsDemoExpired = DateTime.UtcNow.Date > demoUntil.Value.Date;
        }

        internal LicensePayload Payload { get; }
        internal bool IsSignatureValid { get; }
        internal bool IsDemo { get; }
        internal bool IsDemoExpired { get; }
        internal string Error { get; }

        internal static LicenseInfo Load(AppConfig config, string baseDir)
        {
            if (config == null)
            {
                return Invalid("Config missing.");
            }

            string licensePath = config.ResolveLicensePath(baseDir);
            if (!File.Exists(licensePath))
            {
                return Invalid("License file not found.");
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(licensePath));
                var payloadToken = root["payload"] as JObject;
                var signature = (string)root["signature"];
                var algorithm = (string)root["signatureAlgorithm"];

                if (payloadToken == null || string.IsNullOrWhiteSpace(signature))
                {
                    return Invalid("License payload or signature missing.");
                }

                if (!string.IsNullOrWhiteSpace(algorithm)
                    && !string.Equals(algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid("Unsupported signature algorithm.");
                }

                var payload = LicensePayload.FromJObject(payloadToken);
                string dataToSign = LicensePayload.SerializeForSigning(payload);
                bool signatureValid = VerifySignature(dataToSign, signature, config.ResolvePublicKeyPath(baseDir), out string error);

                return new LicenseInfo(payload, signatureValid, error);
            }
            catch (Exception ex)
            {
                return Invalid("License parse error: " + ex.Message);
            }
        }

        private static LicenseInfo Invalid(string error)
        {
            return new LicenseInfo(null, false, error);
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime exact))
            {
                return DateTime.SpecifyKind(exact, DateTimeKind.Utc);
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return null;
        }

        private static bool VerifySignature(string data, string signatureBase64, string publicKeyPath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(publicKeyPath) || !File.Exists(publicKeyPath))
            {
                error = "Public key file not found.";
                return false;
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signatureBase64);
            }
            catch (FormatException)
            {
                error = "Signature is not valid base64.";
                return false;
            }

            try
            {
                string publicKeyXml = File.ReadAllText(publicKeyPath);
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                using (var rsa = new RSACryptoServiceProvider())
                using (var sha = SHA256.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(dataBytes, sha, signatureBytes);
                }
            }
            catch (Exception ex)
            {
                error = "Signature verification failed: " + ex.Message;
                return false;
            }
        }
    }

    internal sealed class LicensePayload
    {
        private LicensePayload(
            string licenseId,
            string product,
            string edition,
            string customerName,
            string customerId,
            string contactEmail,
            string issueDate,
            bool perpetualUse,
            string supportUntil,
            string updatesUntil,
            string demoUntil,
            IList<string> features,
            string maxVersion)
        {
            LicenseId = licenseId;
            Product = product;
            Edition = edition;
            CustomerName = customerName;
            CustomerId = customerId;
            ContactEmail = contactEmail;
            IssueDate = issueDate;
            PerpetualUse = perpetualUse;
            SupportUntil = supportUntil;
            UpdatesUntil = updatesUntil;
            DemoUntil = demoUntil;
            Features = features ?? new List<string>();
            MaxVersion = maxVersion;
        }

        internal string LicenseId { get; }
        internal string Product { get; }
        internal string Edition { get; }
        internal string CustomerName { get; }
        internal string CustomerId { get; }
        internal string ContactEmail { get; }
        internal string IssueDate { get; }
        internal bool PerpetualUse { get; }
        internal string SupportUntil { get; }
        internal string UpdatesUntil { get; }
        internal string DemoUntil { get; }
        internal IList<string> Features { get; }
        internal string MaxVersion { get; }

        internal static LicensePayload FromJObject(JObject payload)
        {
            var features = new List<string>();
            if (payload["features"] is JArray featureArray)
            {
                foreach (var item in featureArray)
                {
                    if (item != null)
                    {
                        features.Add(item.ToString());
                    }
                }
            }

            return new LicensePayload(
                licenseId: (string)payload["licenseId"],
                product: (string)payload["product"],
                edition: (string)payload["edition"],
                customerName: (string)payload["customerName"],
                customerId: (string)payload["customerId"],
                contactEmail: (string)payload["contactEmail"],
                issueDate: (string)payload["issueDate"],
                perpetualUse: payload.Value<bool?>("perpetualUse") ?? false,
                supportUntil: (string)payload["supportUntil"],
                updatesUntil: (string)payload["updatesUntil"],
                demoUntil: (string)payload["demoUntil"],
                features: features,
                maxVersion: (string)payload["maxVersion"]);
        }

        internal static string SerializeForSigning(LicensePayload payload)
        {
            var obj = new JObject
            {
                ["licenseId"] = payload.LicenseId,
                ["product"] = payload.Product,
                ["edition"] = payload.Edition,
                ["customerName"] = payload.CustomerName,
                ["customerId"] = payload.CustomerId,
                ["contactEmail"] = payload.ContactEmail,
                ["issueDate"] = payload.IssueDate,
                ["perpetualUse"] = payload.PerpetualUse,
                ["supportUntil"] = payload.SupportUntil,
                ["updatesUntil"] = payload.UpdatesUntil,
                ["demoUntil"] = payload.DemoUntil,
                ["features"] = new JArray(payload.Features ?? new List<string>()),
                ["maxVersion"] = payload.MaxVersion
            };

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    internal static class LicenseManager
    {
        internal static AppConfig Config { get; private set; }
        internal static LicenseInfo Current { get; private set; }

        internal static bool RequiresDemoWatermark
            => Current != null && (!Current.IsSignatureValid || Current.IsDemoExpired);

        internal static void Initialize(string baseDir)
        {
            Config = AppConfig.Load(baseDir);
            Current = LicenseInfo.Load(Config, baseDir);
        }
    }
}
#pragma warning restore SPELL
