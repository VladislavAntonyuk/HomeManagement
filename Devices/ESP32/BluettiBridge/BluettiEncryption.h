#pragma once

#include <Arduino.h>
#include <vector>

// The encrypted Bluetooth protocol of the newer Bluetti firmware, ported from
// github.com/Patrick762/bluetti-bt-lib (bluetti_bt_lib/bluetooth/encryption.py).
//
// Right after the connection the power station sends a challenge. The answer derives a temporary AES-128 key from it,
// then both sides exchange signed P-256 public keys, and the ECDH secret becomes the AES-256 key for the Modbus frames.
// Messages of the key exchange start with "**" and end with a checksum. Encrypted messages start with the length of
// the plain data, followed by a 4 byte IV seed once the key exchange is done, and the AES-CBC data.
class BluettiEncryption {
public:
  // True once the power station started the key exchange.
  bool isActive() const;

  // True once Modbus frames can be sent.
  bool isReady() const;

  void reset();

  // What the key exchange waits for, e.g. for the error when it does not finish.
  String progress() const;

  // Handles a notification. Fills reply with a message to write back during the key exchange,
  // and message with a decrypted Modbus frame. Both stay empty while a message is incomplete.
  bool handle(const std::vector<uint8_t>& notification, std::vector<uint8_t>& reply, std::vector<uint8_t>& message, String& error);

  // Encrypts a Modbus frame with the key of the key exchange.
  std::vector<uint8_t> encrypt(const uint8_t* data, size_t length);

private:
  bool _isActive = false;
  bool _hasHandshakeKey = false;
  bool _hasPeerPublicKey = false;
  bool _isReady = false;
  uint8_t _handshakeKey[16];
  uint8_t _handshakeIv[16];
  uint8_t _secureKey[32];
  uint8_t _privateKey[32];
  uint8_t _peerPublicKey[64];
  std::vector<uint8_t> _buffer;

  bool handleKeyExchange(const std::vector<uint8_t>& message, std::vector<uint8_t>& reply, String& error);
  bool answerChallenge(const uint8_t* data, size_t length, std::vector<uint8_t>& reply, String& error);
  bool answerPublicKey(const uint8_t* data, size_t length, std::vector<uint8_t>& reply, String& error);
  bool acceptKey(const uint8_t* data, size_t length, String& error);
  bool decrypt(const std::vector<uint8_t>& data, std::vector<uint8_t>& plain, String& error);
  std::vector<uint8_t> encrypt(const uint8_t* data, size_t length, const uint8_t* key, size_t keyLength, const uint8_t* iv);
  size_t expectedLength() const;
};
