# Bluetooth GPIO Controller

What you need?
- Raspberry Pi (any model with GPIO pins)
- Relay Module (5V or 3.3V) (https://radiostore.com.ua/ua/p229350226-modul-rele-kanalnyj.html)

## Raspberry Pi GPIO Pinout
- VCC → Connect to 5V on the Raspberry Pi (or 3.3V if your module supports it).
- GND → Connect to GND on the Raspberry Pi.
- IN → Connect to a GPIO pin on the Raspberry Pi (e.g., GPIO17).

## Relay Module Pinout

The NC (Normally Closed), COM (Common), and NO (Normally Open) terminals on your relay are used to control power to your device (e.g., a light). Here's what they mean:

- COM (Common): This is the main terminal where power comes in.

- NO (Normally Open): The circuit is open when the relay is off and closes when the relay is activated. Use this if you want the device to turn on when the relay is activated.

- NC (Normally Closed): The circuit is closed when the relay is off and opens when the relay is activated. Use this if you want the device to turn off when the relay is activated.

### How to connect it to a light socket:

- Live Wire from Power Source → COM
- NO → Live Wire to the Light
- Neutral Wire → Connect directly to the Light