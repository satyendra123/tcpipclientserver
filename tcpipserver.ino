#include <SPI.h>
#include <Ethernet.h>
#include <avr/wdt.h>

#define relayPin 9

byte mac[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xED};
IPAddress ip(192, 168, 1, 157);

EthernetServer server(7000);
EthernetClient client;

String clientData = "";
bool IsClientConnected = false;

unsigned long currentMillis, previousMillis, reconnectMillis, lastClientActivity;
const unsigned long healthPacketInterval = 3000;
const unsigned long reconnectInterval = 5000;
const unsigned long inactivityTimeout = 10000;

void setup() {
  wdt_disable();
  delay(100);
  wdt_enable(WDTO_8S);

  Ethernet.begin(mac, ip);
  server.begin();
  Serial.begin(9600);

  while (!Serial) { ; }

  Serial.print("Machine Gate IP: ");
  Serial.println(Ethernet.localIP());

  pinMode(relayPin, OUTPUT);
  digitalWrite(relayPin, HIGH);

  IsClientConnected = false;
  currentMillis = previousMillis = reconnectMillis = lastClientActivity = millis();
}

void loop() {
  wdt_reset();

  currentMillis = millis();

  if (!IsClientConnected) {
    if (!client.connected()) {
      client.stop();
    }

    EthernetClient newClient = server.available();
    if (newClient) {
      client = newClient;
      IsClientConnected = true;
      client.flush();
      lastClientActivity = currentMillis;
      Serial.println("Client Connected");
      client.println("Connected to Arduino");
    }
  }

  if (IsClientConnected && client.connected()) {
    while (client.available() > 0) {
      char thisChar = client.read();
      lastClientActivity = currentMillis;

      if (thisChar == '|') {
        clientData = "";
      } else if (thisChar == '%') {
        Serial.print("Received: ");
        Serial.println(clientData);

        if (clientData.equals("OPENEN")) {
          Serial.println("Barrier is opening");
          digitalWrite(relayPin, LOW);
          delay(500);
          digitalWrite(relayPin, HIGH);
          delay(500);
        }
        clientData = "";
      } else {
        if (clientData.length() < 50) {
          clientData += thisChar;
        }
      }
    }

    if (currentMillis - previousMillis >= healthPacketInterval) {
      previousMillis = currentMillis;
      client.println("|HLT%");
    }

    if (currentMillis - lastClientActivity > inactivityTimeout) {
      Serial.println("Client timed out.");
      client.stop();
      IsClientConnected = false;
      server.begin();
    }
  }

  if (IsClientConnected && !client.connected()) {
    Serial.println("Client Disconnected");
    IsClientConnected = false;
    client.stop();
    server.begin();
  }

  if (!IsClientConnected && (currentMillis - reconnectMillis >= reconnectInterval)) {
    reconnectMillis = currentMillis;
    if (Ethernet.linkStatus() == LinkOFF) {
      Serial.println("Ethernet link is down.");
    } else {
      Serial.println("Waiting for client...");
    }
  }
}
