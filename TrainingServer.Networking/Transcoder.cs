using System.Security.Cryptography;

namespace TrainingServer.Networking;

public class Transcoder
{
	readonly RSA _asymmetricProvider = RSA.Create();
	Dictionary<Guid, byte[]> _symmetricKeys = new();

	public RSAParameters GetAsymmetricKey() => _asymmetricProvider.ExportParameters(false);
	public void LoadAsymmetricKey(RSAParameters serverKeyParams) => _asymmetricProvider.ImportParameters(serverKeyParams);
}