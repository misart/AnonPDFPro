using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
            string defaultTheme,
            string licenseId)
        {
            LicenseFile = licenseFile;
            PublicKeyFile = publicKeyFile;
            ServerBaseUrl = serverBaseUrl;
            DefaultTheme = defaultTheme;
            LicenseId = licenseId;
        }

        internal string LicenseFile { get; }
        internal string PublicKeyFile { get; }
        internal string ServerBaseUrl { get; }
        internal string DefaultTheme { get; }
        internal string LicenseId { get; }

        internal static AppConfig Load(string baseDir)
        {
            string configPath = Path.Combine(baseDir, "config.json");
            var defaults = new AppConfig(
                licenseFile: "license.json",
                publicKeyFile: "license_public.xml",
                serverBaseUrl: "https://misart.pl/anonpdfpro",
                defaultTheme: string.Empty,
                licenseId: string.Empty);

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
                    defaultTheme: (string)root["defaultTheme"] ?? defaults.DefaultTheme,
                    licenseId: (string)root["licenseId"] ?? defaults.LicenseId);
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
            string maxVersion,
            bool hasUpdatesUntil)
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
            HasUpdatesUntil = hasUpdatesUntil;
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
        internal bool HasUpdatesUntil { get; }

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

            bool hasUpdatesUntil = payload.Property("updatesUntil") != null;

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
                maxVersion: (string)payload["maxVersion"],
                hasUpdatesUntil: hasUpdatesUntil);
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
                ["demoUntil"] = payload.DemoUntil,
                ["features"] = new JArray(payload.Features ?? new List<string>()),
                ["maxVersion"] = payload.MaxVersion
            };

            if (payload.HasUpdatesUntil)
            {
                obj["updatesUntil"] = payload.UpdatesUntil;
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    internal static class LicenseManager
    {
        internal static AppConfig Config { get; private set; }
        internal static LicenseInfo Current { get; private set; }
        internal static bool IsUpdateOutOfRangeForCurrentVersion { get; private set; }
        internal static DateTime? CurrentBuildDate { get; private set; }
        internal static DateTime? ServerSupportUntil { get; private set; }
        internal static bool IsRevoked { get; private set; }
        internal static string ServerMessage { get; private set; }

        private static readonly HttpClient LicenseHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        internal static bool RequiresDemoWatermark
            => Current != null && (!Current.IsSignatureValid || Current.IsDemoExpired || IsUpdateOutOfRangeForCurrentVersion || IsRevoked);

        internal static bool IsDemoModeForCurrentVersion
            => Current != null && (Current.IsDemo || IsUpdateOutOfRangeForCurrentVersion || IsRevoked);

        internal static DateTime? GetEffectiveSupportUntil()
        {
            if (ServerSupportUntil.HasValue)
            {
                return ServerSupportUntil;
            }

            var info = Current;
            if (info == null || !info.IsSignatureValid || info.Payload == null)
            {
                return null;
            }

            var supportUntil = ParseDate(info.Payload.SupportUntil);
            if (supportUntil.HasValue)
            {
                return supportUntil;
            }

            return ParseDate(info.Payload.UpdatesUntil);
        }

        internal static void Initialize(string baseDir)
        {
            Config = AppConfig.Load(baseDir);
            Current = LicenseInfo.Load(Config, baseDir);
            CurrentBuildDate = GetBuildDateFromFileVersion();
            RefreshUpdateRange();
        }

        internal static bool RefreshServerStatus()
        {
            if (Config == null)
            {
                return false;
            }

            string licenseId = Config.LicenseId;
            if (string.IsNullOrWhiteSpace(licenseId) && Current?.Payload != null)
            {
                licenseId = Current.Payload.LicenseId;
            }

            if (string.IsNullOrWhiteSpace(Config.ServerBaseUrl) || string.IsNullOrWhiteSpace(licenseId))
            {
                return false;
            }

            try
            {
                string url = Config.ServerBaseUrl.TrimEnd('/') + "/clients/" + licenseId + ".json";
                var response = LicenseHttpClient.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var obj = JObject.Parse(json);
                var updatesUntil = ParseDate((string)obj["supportUntil"] ?? (string)obj["updatesUntil"]);
                bool? revoked = null;
                var revokedToken = obj["revoked"];
                if (revokedToken != null && bool.TryParse(revokedToken.ToString(), out bool revokedValue))
                {
                    revoked = revokedValue;
                }
                string message = (string)obj["message"];
                return UpdateServerStatus(updatesUntil, revoked, message);
            }
            catch
            {
                return false;
            }
        }

        private static bool UpdateServerStatus(DateTime? supportUntil, bool? revoked, string message)
        {
            bool changed = false;
            if (!Nullable.Equals(ServerSupportUntil, supportUntil))
            {
                ServerSupportUntil = supportUntil;
                changed = true;
            }

            if (revoked.HasValue && revoked.Value != IsRevoked)
            {
                IsRevoked = revoked.Value;
                changed = true;
            }

            if (message != null && !string.Equals(ServerMessage, message, StringComparison.Ordinal))
            {
                ServerMessage = message;
                changed = true;
            }

            RefreshUpdateRange();
            return changed;
        }

        private static void RefreshUpdateRange()
        {
            IsUpdateOutOfRangeForCurrentVersion = CheckUpdatesOutOfRange(Current, CurrentBuildDate, ServerSupportUntil);
        }

        private static bool CheckUpdatesOutOfRange(LicenseInfo info, DateTime? buildDate, DateTime? supportUntilOverride)
        {
            if (info == null || !info.IsSignatureValid || info.Payload == null)
            {
                return false;
            }

            if (info.IsDemo)
            {
                return false;
            }

            var supportUntil = supportUntilOverride ?? ParseDate(info.Payload.SupportUntil);
            if (!supportUntil.HasValue)
            {
                supportUntil = ParseDate(info.Payload.UpdatesUntil);
            }

            if (!supportUntil.HasValue || !buildDate.HasValue)
            {
                return false;
            }

            return buildDate.Value.Date > supportUntil.Value.Date;
        }

        private static DateTime? GetBuildDateFromFileVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
                return ParseBuildDate(version);
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? ParseBuildDate(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var parts = version.Split('.');
            if (parts.Length < 3)
            {
                return null;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int yearTwoDigit))
            {
                return null;
            }

            string buildPart = parts[2].PadLeft(4, '0');
            if (buildPart.Length < 4)
            {
                return null;
            }

            if (!int.TryParse(buildPart.Substring(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int month))
            {
                return null;
            }
            if (!int.TryParse(buildPart.Substring(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int day))
            {
                return null;
            }

            int year = 2000 + yearTwoDigit;
            if (month < 1 || month > 12 || day < 1 || day > 31)
            {
                return null;
            }

            return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
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
    }
}
#pragma warning restore SPELL
