#include <Arduino.h>
#include <SPIFFS.h>
#include <ml_reader.hpp>

using namespace io;
using namespace ml;
File file;
ml_reader_ex<2048> reader;
void setup() {
    Serial.begin(115200);
    SPIFFS.begin();
    // use binary mode so utf-8 works
    file = SPIFFS.open("/books.xml","rb");
    file_stream fs(file);
    reader.set(fs);
    while(reader.read()) {
        if(reader.node_type()==ml_node_type::element) {
            printf("element: %s\n",reader.value());
            reader.read();
            if(reader.node_type()==ml_node_type::attribute) {
                while(reader.node_type()==ml_node_type::attribute) {
                    Serial.printf("\tattribute: %s = ",reader.value());
                    reader.read();
                    while(reader.node_type()==ml_node_type::attribute_content) {
                        Serial.printf("%s",reader.value());
                        reader.read();
                    }
                    if(reader.node_type()==ml_node_type::attribute_end) {
                        reader.read();
                    }
                    Serial.println("");
                }
            }
            while(reader.node_type()==ml_node_type::content) {
                Serial.printf("\tcontent: %s\n",reader.value());
                reader.read();
            }
        } 
    }
    fs.close();
}

void loop() {
  
}
