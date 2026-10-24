const int motionPin = 23; 

// Змінна для зберігання попереднього стану (початково HIGH, оскільки використовується PULLUP)
int lastMotionState = HIGH; 

void setup() {
  Serial.begin(19200);
  delay(1000);
  
  // Налаштовуємо пін як вхід із внутрішньою підтяжкою до 3.3V
  pinMode(motionPin, INPUT_PULLUP);
  
  Serial.println("Initialized...");
}

void loop() {
  int currentMotionState = digitalRead(motionPin);

  if (currentMotionState != lastMotionState) {
    lastMotionState = currentMotionState;

    // Оскільки реле замикає пін на GND при спрацюванні:
    // LOW = реле замкнулося (датчик зафіксував рух)
    // HIGH = реле розімкнулося (руху немає)
    if (currentMotionState == LOW) {
      Serial.println("Move detected!");
    } else {
      Serial.println("No motion.");
    }
    
    // Коротка затримка (дебаунс) для захисту від дребезгу контактів реле
    delay(50); 
  }
}