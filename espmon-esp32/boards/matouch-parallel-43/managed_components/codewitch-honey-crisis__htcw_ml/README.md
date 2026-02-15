# ml

A pull parser for reading markup

example:
```cpp
#include <stdio.h>
#include <ml_reader.hpp>
using namespace io;
using namespace ml;
int main(int argc, char **argv) {
    file_stream fs("data\books.xml");
    ml_reader_ex<2048> reader;
    reader.set(fs);
    while(reader.read()) {
        if(reader.node_type()==ml_node_type::element) {
            int depth = reader.depth();
            printf("element: %s\n",reader.value());
            reader.read();
            if(reader.node_type()==ml_node_type::attribute) {
                while(reader.node_type()==ml_node_type::attribute) {
                    printf("\tattribute: %s = ",reader.value());
                    reader.read();
                    while(reader.node_type()==ml_node_type::attribute_content) {
                        printf("%s",reader.value());
                        reader.read();
                    }
                    if(reader.node_type()==ml_node_type::attribute_end) {
                        reader.read();
                    }
                    puts("");
                }
            }
            while(reader.node_type()==ml_node_type::content) {
                printf("\tcontent: %s\n",reader.value());
                reader.read();
            }
        } 
    }
    fs.close();
    return 1;
}
```

platformio.ini:
```
[env:node32s]
platform = espressif32
board = node32s
framework = arduino
lib_deps = 
	codewitch-honey-crisis/htcw_ml
```