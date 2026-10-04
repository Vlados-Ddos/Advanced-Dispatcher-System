using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdvancedDispatcherSystem.Host;

// Each installation has its own private CA. Trust is an explicit client action;
// the Host never changes the operating system's trust store.
public sealed class LocalCertificate : IDisposable
{
    public X509Certificate2 Server { get; }
    public X509Certificate2 Authority { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Authority.RawData));

    public LocalCertificate(string directory, IEnumerable<string> hosts)
    {
        Directory.CreateDirectory(directory);
        string caPath = Path.Combine(directory, "dispatcher-ca.pfx");
        if (File.Exists(caPath)) Authority = Load(caPath);
        else
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=Advanced Dispatch " + Guid.NewGuid().ToString("N"), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            Save(caPath, created.Export(X509ContentType.Pfx));
            Authority = Load(caPath);
        }
        if (!Authority.HasPrivateKey || Authority.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(1))
            throw new InvalidDataException("HTTPS_CA_EXPIRED_OR_KEY_MISSING: restore or explicitly replace dispatcher-ca.pfx; existing client trust is preserved until then.");
        Save(Path.Combine(directory, "dispatcher-ca.cer"), Authority.Export(X509ContentType.Cert));
        string serverPath = Path.Combine(directory, "server.pfx");
        var names = hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        X509Certificate2 existing = File.Exists(serverPath) ? Load(serverPath) : null;
        if (existing != null && Valid(existing, Authority, names)) { Server = existing; return; }
        existing?.Dispose();
        using var serverKey = RSA.Create(3072);
        var serverRequest = new CertificateRequest("CN=Advanced Dispatch", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in names) { if (IPAddress.TryParse(name, out var ip)) san.AddIpAddress(ip); else san.AddDnsName(name); }
        serverRequest.CertificateExtensions.Add(san.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var until = DateTimeOffset.UtcNow.AddYears(1);
        if (until.UtcDateTime > Authority.NotAfter.ToUniversalTime()) until = Authority.NotAfter.ToUniversalTime();
        using var signed = serverRequest.Create(Authority, DateTimeOffset.UtcNow.AddDays(-1), until, RandomNumberGenerator.GetBytes(16));
        using var withKey = signed.CopyWithPrivateKey(serverKey);
        // Keep a recoverable old leaf on migration/renewal. Never rotate the CA
        // because importing a key failed or a network adapter changed.
        if (File.Exists(serverPath)) File.Copy(serverPath, serverPath + ".previous", true);
        Save(serverPath, withKey.Export(X509ContentType.Pfx));
        Server = Load(serverPath);
    }

    private static X509Certificate2 Load(string path) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.DefaultKeySet);

    private static bool Valid(X509Certificate2 leaf, X509Certificate2 ca, string[] hosts)
    {
        if (!leaf.HasPrivateKey || leaf.NotBefore.ToUniversalTime() > DateTime.UtcNow || leaf.NotAfter.ToUniversalTime() < DateTime.UtcNow.AddDays(7)) return false;
        var extension = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (extension == null) return false;
        var ips = extension.EnumerateIPAddresses().ToHashSet();
        var dns = extension.EnumerateDnsNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (hosts.Any(h => IPAddress.TryParse(h, out var ip) ? !ips.Contains(ip) : !dns.Contains(h))) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(leaf);
    }

    private static void Save(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        string temporary = path + ".new";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    public void Dispose() { Server?.Dispose(); Authority?.Dispose(); }
}
