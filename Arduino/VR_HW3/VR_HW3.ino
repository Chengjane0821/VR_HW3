const int potPin = A0;
const int ledPin = 4;

unsigned long lastSendTime = 0;
const unsigned long sendInterval = 50;

String command = "";

void setup() {
  Serial.begin(9600);

  pinMode(potPin, INPUT);
  pinMode(ledPin, OUTPUT);

  digitalWrite(ledPin, LOW);
}

void loop() {

  // Part 1: Arduino -> Unity
  if (millis() - lastSendTime >= sendInterval) {

    lastSendTime = millis();

    int potValue = analogRead(potPin);

    Serial.print("POT:");
    Serial.println(potValue);
  }

  // Part 2: Unity -> Arduino
  while (Serial.available() > 0) {

    char received = Serial.read();

    if (received == '\n') {

      command.trim();

      if (command == "LED:1") {
        digitalWrite(ledPin, HIGH);
      }
      else if (command == "LED:0") {
        digitalWrite(ledPin, LOW);
      }

      command = "";
    }
    else if (received != '\r') {

      command += received;

      if (command.length() > 32) {
        command = "";
      }
    }
  }
}
