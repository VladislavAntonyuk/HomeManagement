#include "BluettiEncryption.h"

#include <esp_random.h>
#include <mbedtls/aes.h>
#include <mbedtls/ecdh.h>
#include <mbedtls/ecdsa.h>
#include <mbedtls/md5.h>
#include <mbedtls/sha256.h>

// The fixed keys of the Bluetti app, the same for every power station.
static const char* const LocalAesKey = "459FC535808941F17091E0993EE3E93D";
// Signs our public key.
static const char* const SigningPrivateKey = "4F19A16E3E87BDD9BD24D3E5495B88041511943CBC8B969ADE9641D0F56AF337";
// Verifies the public key of the power station: X followed by Y.
static const char* const PeerSigningPublicKey =
  "A73ABF5D2232C8C1C72E68304343C272495E3A8FD6F30EA96DE2F4B3CE60B251"
  "EE21AC667CF8A71E18B46B664EAEFFE3C489F24F695B6411DB7E22CCC85A8594";

static const uint8_t KeyExchangeMagic = '*';
static const size_t AesBlockSize = 16;
static const size_t PublicKeySize = 64;
static const size_t SignatureSize = 64;

enum MessageType : uint8_t {
  Challenge = 1,
  ChallengeAccepted = 3,
  PeerPublicKey = 4,
  PublicKeyAccepted = 6
};

static void fromHex(const char* hex, uint8_t* bytes, size_t length) {
  for (size_t i = 0; i < length; i++) {
    char byte[3] = { hex[2 * i], hex[2 * i + 1], 0 };
    bytes[i] = strtoul(byte, nullptr, 16);
  }
}

static int fillRandom(void*, unsigned char* output, size_t length) {
  esp_fill_random(output, length);
  return 0;
}

static void md5(const uint8_t* data, size_t length, uint8_t* digest) {
  mbedtls_md5(data, length, digest);
}

static void sha256(const std::vector<uint8_t>& data, uint8_t* digest) {
  mbedtls_sha256(data.data(), data.size(), digest, 0);
}

static void printHex(const char* label, const uint8_t* data, size_t length) {
  Serial.print(label);
  for (size_t i = 0; i < length; i++) {
    Serial.printf("%02x", data[i]);
  }

  Serial.println();
}

static uint16_t checksum(const uint8_t* data, size_t length) {
  uint16_t sum = 0;
  for (size_t i = 0; i < length; i++) {
    sum += data[i];
  }

  return sum;
}

static bool isKeyExchange(const std::vector<uint8_t>& message) {
  return message.size() >= 2 && message[0] == KeyExchangeMagic && message[1] == KeyExchangeMagic;
}

// "**", the body, and the checksum of the body.
static std::vector<uint8_t> keyExchangeMessage(const std::vector<uint8_t>& body) {
  std::vector<uint8_t> message = { KeyExchangeMagic, KeyExchangeMagic };
  message.insert(message.end(), body.begin(), body.end());
  uint16_t sum = checksum(body.data(), body.size());
  message.push_back(sum >> 8);
  message.push_back(sum & 0xFF);
  return message;
}

// A P-256 curve with the mbedTLS values that live as long as the operation.
struct Curve {
  mbedtls_ecp_group group;
  mbedtls_ecp_point point;
  mbedtls_mpi secret;
  mbedtls_mpi r;
  mbedtls_mpi s;

  Curve() {
    mbedtls_ecp_group_init(&group);
    mbedtls_ecp_point_init(&point);
    mbedtls_mpi_init(&secret);
    mbedtls_mpi_init(&r);
    mbedtls_mpi_init(&s);
    mbedtls_ecp_group_load(&group, MBEDTLS_ECP_DP_SECP256R1);
  }

  ~Curve() {
    mbedtls_ecp_group_free(&group);
    mbedtls_ecp_point_free(&point);
    mbedtls_mpi_free(&secret);
    mbedtls_mpi_free(&r);
    mbedtls_mpi_free(&s);
  }

  // X followed by Y, without the 0x04 prefix of the uncompressed format.
  bool readPoint(const uint8_t* publicKey) {
    uint8_t uncompressed[PublicKeySize + 1] = { 0x04 };
    memcpy(uncompressed + 1, publicKey, PublicKeySize);
    return mbedtls_ecp_point_read_binary(&group, &point, uncompressed, sizeof(uncompressed)) == 0
           && mbedtls_ecp_check_pubkey(&group, &point) == 0;
  }

  bool writePoint(uint8_t* publicKey) {
    uint8_t uncompressed[PublicKeySize + 1];
    size_t length = 0;
    if (mbedtls_ecp_point_write_binary(&group, &point, MBEDTLS_ECP_PF_UNCOMPRESSED, &length, uncompressed, sizeof(uncompressed)) != 0
        || length != sizeof(uncompressed)) {
      return false;
    }

    memcpy(publicKey, uncompressed + 1, PublicKeySize);
    return true;
  }
};

bool BluettiEncryption::isActive() const {
  return _isActive;
}

bool BluettiEncryption::isReady() const {
  return _isReady;
}

void BluettiEncryption::reset() {
  _isActive = false;
  _hasHandshakeKey = false;
  _hasPeerPublicKey = false;
  _isReady = false;
  _buffer.clear();
}

String BluettiEncryption::progress() const {
  String step = !_hasHandshakeKey    ? "the challenge"
                : !_hasPeerPublicKey ? "the public key of the power station"
                : !_isReady          ? "the power station to accept our public key"
                                     : "nothing";
  size_t length = expectedLength();
  if (length > 0) {
    step += ", " + String(_buffer.size()) + " of " + String(length) + " bytes of a message received";
  }

  return step;
}

bool BluettiEncryption::handle(const std::vector<uint8_t>& notification, std::vector<uint8_t>& reply, std::vector<uint8_t>& message, String& error) {
  reply.clear();
  message.clear();

  if (isKeyExchange(notification)) {
    _isActive = true;
    return handleKeyExchange(notification, reply, error);
  }

  if (!_hasHandshakeKey) {
    error = "Encrypted message before the key exchange.";
    return false;
  }

  // A message can be split across several notifications.
  _buffer.insert(_buffer.end(), notification.begin(), notification.end());
  size_t length = expectedLength();
  if (length == 0 || _buffer.size() < length) {
    return true;
  }

  std::vector<uint8_t> encrypted(_buffer.begin(), _buffer.begin() + length);
  _buffer.erase(_buffer.begin(), _buffer.begin() + length);

  std::vector<uint8_t> plain;
  if (!decrypt(encrypted, plain, error)) {
    _buffer.clear();
    return false;
  }

  if (isKeyExchange(plain)) {
    return handleKeyExchange(plain, reply, error);
  }

  message = plain;
  return true;
}

std::vector<uint8_t> BluettiEncryption::encrypt(const uint8_t* data, size_t length) {
  uint8_t seed[4];
  esp_fill_random(seed, sizeof(seed));
  uint8_t iv[16];
  md5(seed, sizeof(seed), iv);

  std::vector<uint8_t> message = encrypt(data, length, _secureKey, sizeof(_secureKey), iv);
  message.insert(message.begin() + 2, seed, seed + sizeof(seed));
  return message;
}

bool BluettiEncryption::handleKeyExchange(const std::vector<uint8_t>& message, std::vector<uint8_t>& reply, String& error) {
  // "**", type, length, data, checksum.
  if (message.size() < 6) {
    error = "Key exchange message too short.";
    return false;
  }

  const uint8_t* body = message.data() + 2;
  size_t bodyLength = message.size() - 4;
  uint16_t expectedChecksum = (message[message.size() - 2] << 8) | message[message.size() - 1];
  // bluetti-bt-lib only logs a wrong checksum, so this does too instead of failing a key exchange that works.
  if (checksum(body, bodyLength) != expectedChecksum) {
    Serial.printf("Bluetti: invalid checksum of key exchange message %u.\n", body[0]);
  }

  const uint8_t* data = body + 2;
  size_t dataLength = bodyLength - 2;
  Serial.printf("Bluetti: key exchange message %u with %u bytes of data.\n", body[0], dataLength);
  switch (body[0]) {
    case Challenge:
      return answerChallenge(data, dataLength, reply, error);
    case PeerPublicKey:
      return answerPublicKey(data, dataLength, reply, error);
    case PublicKeyAccepted:
      return acceptKey(data, dataLength, error);
    case ChallengeAccepted:
    default:
      return true;
  }
}

bool BluettiEncryption::answerChallenge(const uint8_t* data, size_t length, std::vector<uint8_t>& reply, String& error) {
  if (length != 4) {
    error = "Unexpected length of the challenge.";
    return false;
  }

  _hasPeerPublicKey = false;
  _isReady = false;
  _buffer.clear();

  uint8_t reversed[4] = { data[3], data[2], data[1], data[0] };
  md5(reversed, sizeof(reversed), _handshakeIv);

  uint8_t localKey[16];
  fromHex(LocalAesKey, localKey, sizeof(localKey));
  for (size_t i = 0; i < sizeof(_handshakeKey); i++) {
    _handshakeKey[i] = _handshakeIv[i] ^ localKey[i];
  }

  _hasHandshakeKey = true;
  Serial.println("Bluetti: challenge received.");
  reply = keyExchangeMessage({ 0x02, 0x04, _handshakeIv[8], _handshakeIv[9], _handshakeIv[10], _handshakeIv[11] });
  return true;
}

bool BluettiEncryption::answerPublicKey(const uint8_t* data, size_t length, std::vector<uint8_t>& reply, String& error) {
  if (length != PublicKeySize + SignatureSize) {
    error = "Unexpected length of the public key of the power station.";
    return false;
  }

  // The power station signs its public key followed by the IV of the challenge.
  std::vector<uint8_t> signedData(data, data + PublicKeySize);
  signedData.insert(signedData.end(), _handshakeIv, _handshakeIv + sizeof(_handshakeIv));
  uint8_t hash[32];
  sha256(signedData, hash);

  {
    Curve verifier;
    uint8_t signingKey[PublicKeySize];
    fromHex(PeerSigningPublicKey, signingKey, sizeof(signingKey));
    if (!verifier.readPoint(signingKey)
        || mbedtls_mpi_read_binary(&verifier.r, data + PublicKeySize, 32) != 0
        || mbedtls_mpi_read_binary(&verifier.s, data + PublicKeySize + 32, 32) != 0
        || mbedtls_ecdsa_verify(&verifier.group, hash, sizeof(hash), &verifier.point, &verifier.r, &verifier.s) != 0) {
      error = "Invalid signature of the public key of the power station.";
      return false;
    }
  }

  {
    Curve peer;
    if (!peer.readPoint(data)) {
      error = "Invalid public key of the power station.";
      return false;
    }
  }

  memcpy(_peerPublicKey, data, PublicKeySize);
  _hasPeerPublicKey = true;
  Serial.println("Bluetti: public key of the power station verified.");

  uint8_t publicKey[PublicKeySize];
  {
    Curve keyPair;
    if (mbedtls_ecdh_gen_public(&keyPair.group, &keyPair.secret, &keyPair.point, fillRandom, nullptr) != 0
        || !keyPair.writePoint(publicKey)
        || mbedtls_mpi_write_binary(&keyPair.secret, _privateKey, sizeof(_privateKey)) != 0) {
      error = "Could not generate a key pair.";
      return false;
    }
  }

  // Our public key followed by the IV of the challenge, signed with the key of the Bluetti app.
  std::vector<uint8_t> toSign(publicKey, publicKey + PublicKeySize);
  toSign.insert(toSign.end(), _handshakeIv, _handshakeIv + sizeof(_handshakeIv));
  sha256(toSign, hash);

  uint8_t signature[SignatureSize];
  {
    Curve signer;
    uint8_t signingKey[32];
    fromHex(SigningPrivateKey, signingKey, sizeof(signingKey));
    if (mbedtls_mpi_read_binary(&signer.secret, signingKey, sizeof(signingKey)) != 0
        || mbedtls_ecdsa_sign(&signer.group, &signer.r, &signer.s, &signer.secret, hash, sizeof(hash), fillRandom, nullptr) != 0
        || mbedtls_mpi_write_binary(&signer.r, signature, 32) != 0
        || mbedtls_mpi_write_binary(&signer.s, signature + 32, 32) != 0) {
      error = "Could not sign the public key.";
      return false;
    }
  }

  std::vector<uint8_t> body = { 0x05, 0x80 };
  body.insert(body.end(), publicKey, publicKey + PublicKeySize);
  body.insert(body.end(), signature, signature + SignatureSize);
  std::vector<uint8_t> message = keyExchangeMessage(body);
  reply = encrypt(message.data(), message.size(), _handshakeKey, sizeof(_handshakeKey), _handshakeIv);
  printHex("Bluetti: our signed public key, IV ", _handshakeIv, sizeof(_handshakeIv));
  printHex("Bluetti: our signed public key, message ", message.data(), message.size());
  return true;
}

bool BluettiEncryption::acceptKey(const uint8_t* data, size_t length, String& error) {
  if (length != 1 || data[0] != 0) {
    error = "The power station did not accept the public key.";
    return false;
  }

  Curve curve;
  if (!curve.readPoint(_peerPublicKey)
      || mbedtls_mpi_read_binary(&curve.r, _privateKey, sizeof(_privateKey)) != 0
      || mbedtls_ecdh_compute_shared(&curve.group, &curve.secret, &curve.point, &curve.r, fillRandom, nullptr) != 0
      || mbedtls_mpi_write_binary(&curve.secret, _secureKey, sizeof(_secureKey)) != 0) {
    error = "Could not compute the shared key.";
    return false;
  }

  _isReady = true;
  Serial.println("Bluetti: key exchange done.");
  return true;
}

bool BluettiEncryption::decrypt(const std::vector<uint8_t>& data, std::vector<uint8_t>& plain, String& error) {
  size_t plainLength = (data[0] << 8) | data[1];
  uint8_t iv[16];
  const uint8_t* key;
  size_t keyLength;
  size_t headerLength;
  if (_isReady) {
    md5(data.data() + 2, 4, iv);
    key = _secureKey;
    keyLength = sizeof(_secureKey);
    headerLength = 6;
  } else {
    memcpy(iv, _handshakeIv, sizeof(iv));
    key = _handshakeKey;
    keyLength = sizeof(_handshakeKey);
    headerLength = 2;
  }

  size_t encryptedLength = data.size() - headerLength;
  if (encryptedLength % AesBlockSize != 0 || plainLength > encryptedLength) {
    error = "Encrypted message not aligned to the AES block size.";
    return false;
  }

  plain.resize(encryptedLength);
  mbedtls_aes_context aes;
  mbedtls_aes_init(&aes);
  bool isDecrypted = mbedtls_aes_setkey_dec(&aes, key, keyLength * 8) == 0
                     && mbedtls_aes_crypt_cbc(&aes, MBEDTLS_AES_DECRYPT, encryptedLength, iv, data.data() + headerLength, plain.data()) == 0;
  mbedtls_aes_free(&aes);
  if (!isDecrypted) {
    error = "Could not decrypt a message.";
    return false;
  }

  plain.resize(plainLength);
  return true;
}

// The length of the plain data, followed by the data padded with zeros to the AES block size.
std::vector<uint8_t> BluettiEncryption::encrypt(const uint8_t* data, size_t length, const uint8_t* key, size_t keyLength, const uint8_t* iv) {
  size_t paddedLength = (length + AesBlockSize - 1) / AesBlockSize * AesBlockSize;
  std::vector<uint8_t> padded(data, data + length);
  padded.resize(paddedLength, 0);

  std::vector<uint8_t> message = { (uint8_t)(length >> 8), (uint8_t)length };
  message.resize(2 + paddedLength);

  uint8_t chainedIv[16];
  memcpy(chainedIv, iv, sizeof(chainedIv));
  mbedtls_aes_context aes;
  mbedtls_aes_init(&aes);
  mbedtls_aes_setkey_enc(&aes, key, keyLength * 8);
  mbedtls_aes_crypt_cbc(&aes, MBEDTLS_AES_ENCRYPT, paddedLength, chainedIv, padded.data(), message.data() + 2);
  mbedtls_aes_free(&aes);
  return message;
}

// The header and the padded data of the message in the buffer, or 0 until the length arrived.
size_t BluettiEncryption::expectedLength() const {
  if (_buffer.size() < 2) {
    return 0;
  }

  size_t plainLength = (_buffer[0] << 8) | _buffer[1];
  size_t headerLength = _isReady ? 6 : 2;
  return headerLength + (plainLength + AesBlockSize - 1) / AesBlockSize * AesBlockSize;
}
