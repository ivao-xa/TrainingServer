namespace TrainingServer.Networking;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using static System.Text.Encoding;

/// <summary>
/// Provides symmetric and asymmetric encryption capabilities for managing secure communications with multiple recipients.
/// </summary>
public class Transcoder
{
	readonly RSA _asymmetricProvider = RSA.Create();
	readonly Aes _symmetricProvider = Aes.Create();
	Dictionary<Guid, Aes>? _symmetricKeys = null;

	/// <summary>Gets the public key for the current instance's asymmetric encryption engine.</summary>
	public RSAParameters GetAsymmetricKey() => _asymmetricProvider.ExportParameters(false);

	/// <summary>Overwrites the keys for the current instance's asymmetric encryption engine.</summary>
	/// <param name="serverKeyParams">The <see cref="RSAParameters"/> containing the new asymmetric key(s).</param>
	public void LoadAsymmetricKey(RSAParameters serverKeyParams) => _asymmetricProvider.ImportParameters(serverKeyParams);

	/// <summary>Performs RSA encryption using the current instance's public key.</summary>
	/// <param name="data">The data to be encrypted.</param>
	/// <returns>The cyphered data.</returns>
	public byte[] AsymmetricEncrypt(byte[] data) => _asymmetricProvider.Encrypt(data, RSAEncryptionPadding.OaepSHA256);

	/// <summary>Performs RSA decryption using the current instance's private key.</summary>
	/// <param name="data">The cyphered data to be decrypted.</param>
	/// <returns>The plaintext data.</returns>
	public byte[] AsymmetricDecrypt(byte[] data) => _asymmetricProvider.Decrypt(data, RSAEncryptionPadding.OaepSHA256);

	/// <summary>Registers a new symmetric encryption tunnel to the current instance.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with the tunnel.</param>
	/// <param name="key">The symmetric AES key securing the tunnel.</param>
	public void RegisterKey(Guid recipient, byte[] key)
	{
		_symmetricKeys ??= new();

		_symmetricKeys[recipient] = Aes.Create();
		_symmetricKeys[recipient].Key = key;
	}

	/// <summary>Unregisters a symmetric encryption tunnal from the current instance.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with the tunnel.</param>
	/// <returns><see langword="true"/> if the tunnel was successfully deregistered, otherwise <see langword="false"/>.</returns>
	public bool TryUnregister(Guid recipient) => _symmetricKeys?.Remove(recipient) ?? throw new InvalidOperationException("Cannot deregister a client secure tunnel when in client mode.");

	/// <summary>Encrypts the given <see cref="string"/> using the key asssociated with the provided <see cref="Guid"/>'s tunnel.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with a registered tunnel.</param>
	/// <param name="data">The <see cref="string"/> to be encrypted.</param>
	/// <returns>The encryped message ready to be sent over the identified tunnel.</returns>
	/// <exception cref="ArgumentException">The given <paramref name="recipient"/> has not been registered. <seealso cref="RegisterKey(Guid, byte[])"/></exception>
	public byte[] SymmetricEncryptString(Guid recipient, string data) => SymmetricEncrypt(recipient, UTF8.GetBytes(data));

	/// <summary>Encrypts the given array of <see cref="byte"/>s using the key asssociated with the provided <see cref="Guid"/>'s tunnel.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with a registered tunnel.</param>
	/// <param name="data">The buffer to be encrypted.</param>
	/// <returns>The encryped message ready to be sent over the identified tunnel.</returns>
	/// <exception cref="ArgumentException">The given <paramref name="recipient"/> has not been registered. <seealso cref="RegisterKey(Guid, byte[])"/></exception>
	public byte[] SymmetricEncrypt(Guid recipient, byte[] data)
	{
		Aes? crypt = _symmetricProvider;

		if (_symmetricKeys is not null && !_symmetricKeys.TryGetValue(recipient, out crypt))
			throw new ArgumentException($"No tunnel set up with {recipient}.", nameof(recipient));

		crypt.GenerateIV();

		return crypt.IV.Concat(crypt.EncryptCbc(data, crypt.IV)).ToArray();
	}

	/// <summary>Decrypts a text message received through an encrypted tunnel.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with a registered tunnel.</param>
	/// <param name="data">The ciphered message buffer.</param>
	/// <returns>The decrypted plaintext.</returns>
	/// <exception cref="ArgumentException">The given <paramref name="recipient"/> has not been registered. <seealso cref="RegisterKey(Guid, byte[])"/></exception>
	public string SymmetricDecryptString(Guid recipient, byte[] data) => UTF8.GetString(SymmetricDecrypt(recipient, data));

	/// <summary>Decrypts a binary message received through an encrypted tunnel.</summary>
	/// <param name="recipient">The <see cref="Guid"/> associated with a registered tunnel.</param>
	/// <param name="data">The ciphered message buffer.</param>
	/// <returns>The decrypted buffer.</returns>
	/// <exception cref="ArgumentException">The given <paramref name="recipient"/> has not been registered. <seealso cref="RegisterKey(Guid, byte[])"/></exception>
	public byte[] SymmetricDecrypt(Guid recipient, byte[] data)
	{
		Aes? crypt = _symmetricProvider;

		if (_symmetricKeys is not null && !_symmetricKeys.TryGetValue(recipient, out crypt))
			throw new ArgumentException($"No tunnel set up with {recipient}.", nameof(recipient));

		return crypt.DecryptCbc(data[crypt.IV.Length..], data[..crypt.IV.Length]);
	}

	/// <summary>Unpacks a message received from a registered tunnel.</summary>
	/// <param name="message">The received message.</param>
	/// <returns>The timestamp and JSON data encoded in the message.</returns>
	/// <exception cref="ArgumentException">The message was not valid.</exception>
	/// <exception cref="CryptographicException">The message could not be sensibly decoded with the given tunnel's key.</exception>
	public (DateTimeOffset Time, JsonNode Data) SecureUnpack(string message)
	{
		if (message.Length < 2 || !message.Contains('}'))
			throw new ArgumentException($"Cannot deserialize message {message}.", nameof(message));

		message = message[..(message.IndexOf("data") + message[message.IndexOf("data")..].IndexOf('}') + 1)];

		if (JsonNode.Parse(message) is not JsonObject jo)
			throw new ArgumentException($"Cannot deserialize message {message}.", nameof(message));

		if (jo["id"]?.GetValue<Guid>() is not Guid tunnel)
			throw new ArgumentException($"Cannot identify tunnel from message {message}.", nameof(message));

		if (jo["time"]?.GetValue<DateTimeOffset>() is not DateTimeOffset time)
			throw new ArgumentException($"Message {message} is missing required timestamp.", nameof(message));

		if (jo["data"]?.GetValue<string>() is not string cryptedString)
			return (time, new JsonObject());

		string packet = SymmetricDecryptString(tunnel, System.Convert.FromBase64String(cryptedString));

		if (JsonNode.Parse(packet) is JsonNode retval)
			return (time, retval);
		else
			throw new CryptographicException("The given message could not be sensibly decoded.");
	}

	/// <summary>Packs a message for a given registered tunnel.</summary>
	/// <param name="recipient">The <see cref="Guid"/> of the tunnel through which the message will be sent.</param>
	/// <param name="message">The message to be sent.</param>
	/// <returns>The encoded message.</returns>
	public string SecurePack(Guid recipient, object message)
	{
		string data = JsonSerializer.Serialize(message);
		byte[] packet = SymmetricEncryptString(recipient, data);
		return JsonSerializer.Serialize(new { id = recipient, time = DateTimeOffset.Now, data = System.Convert.ToBase64String(packet) });
	}
}