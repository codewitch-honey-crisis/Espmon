#ifndef HTCW_ML_ML_READER_FA
#define HTCW_ML_ML_READER_FA
#ifdef ARDUINO
#include <Arduino.h>
#else
#include <stdint.h>
#ifndef PROGMEM
#define PROGMEM
#endif
#endif
namespace ml {
    namespace helpers {
    // size is 34
    extern const uint16_t entity_dfa_state_offsets_xml[] PROGMEM;
    // size is 34
    extern const uint8_t entity_dfa_accept_table_xml[] PROGMEM;
    // size is 192
    extern const uint8_t entity_dfa_transition_table_xml[] PROGMEM;

#ifdef HTCW_ML_HTML_ENTITIES
	// size is 1088
	extern const uint16_t entity_dfa_state_offsets_all[] PROGMEM;
	// size is 1088
	extern const uint16_t entity_dfa_accept_table_all[] PROGMEM;
	// size is 3306
	extern const uint16_t entity_dfa_transition_table_all[] PROGMEM;

#endif // HTCW_ML_HTML_ENTITIES
    }
    enum struct ml_entity_support {
        xml_only = 0,
#ifdef HTCW_ML_HTML_ENTITIES
        all = 1
#endif
    };
    namespace helpers  {
        struct entity_lex_state {
            unsigned int state;
            unsigned int last_match_state;
            unsigned int accept;
            uint32_t* buf;
            unsigned int buflen;
            unsigned int num;
            unsigned int flags;
        };
        // NOTE: constexprs are pretty much useless below, but it was a last ditch to get the compiler to remove dead code
        template<ml_entity_support EntitySupport> struct entity_lexer {};
#ifdef HTCW_ML_HTML_ENTITIES
        template<> struct entity_lexer<ml_entity_support::all> 
        {
            constexpr static const unsigned int none = 0xFFFF;
            constexpr static const unsigned int max_len = 10;
            static int lex_entity_part(uint32_t current, entity_lex_state *s) {
                int st = s->state;
#ifdef ARDUINO
                const uint16_t* ttp = entity_dfa_transition_table_all+pgm_read_word(entity_dfa_state_offsets_all+st);
                while(pgm_read_word(ttp++)==st) {
                    uint32_t cp_range = pgm_read_word(ttp++);
                    int cp_first = (uint8_t)(cp_range & UINT8_MAX);
                    int cp_last = (uint8_t)(cp_range >> 8);
#else
                const uint16_t* ttp = entity_dfa_transition_table_all+entity_dfa_state_offsets_all[st];
                while(*ttp++==st) {
                    uint16_t cp_range = *ttp++;
                    int cp_first = (uint8_t)(cp_range & UINT8_MAX);
                    int cp_last = (uint8_t)(cp_range >> 8);
#endif
                    if(current>=cp_first && current<=cp_last) {
                        *(s->buf+(s->buflen++))=current;
                        s->last_match_state = st;
#ifdef ARDUINO
                        s->state = pgm_read_word(ttp++);
#else
                        s->state = *ttp++;
#endif
                        // tell the caller to advance
                        return 1;
                    } else {
                        ++ttp;
                    }
                }
                return 0;
            }
            inline static uint32_t accept(int state) {
                return entity_dfa_accept_table_all[state];
            }
        };
#endif // HTCW_ML_HTML_ENTITIES
        template<> struct entity_lexer<ml_entity_support::xml_only> 
        {
            constexpr static const unsigned int none = 0xFF;
            constexpr static const unsigned int max_len = 8;
            static int lex_entity_part(uint32_t current, entity_lex_state *s) {
                int st = s->state;
#ifdef ARDUINO
                const uint8_t* ttp=entity_dfa_transition_table_xml+pgm_read_word(entity_dfa_state_offsets_xml+st);
                while(pgm_read_byte(ttp++)==st) {
                    uint32_t cp_first = pgm_read_byte(ttp++);
                    uint32_t cp_last = pgm_read_byte(ttp++);
#else
                const uint8_t* ttp=entity_dfa_transition_table_xml+entity_dfa_state_offsets_xml[st];
                while(*ttp++==st) {
                    uint32_t cp_first = *ttp++;
                    uint32_t cp_last = *ttp++;
#endif
                    if(current>=cp_first && current<=cp_last) {
                        *(s->buf+(s->buflen++))=current;
                        s->last_match_state = st;
#ifdef ARDUINO
                        s->state = pgm_read_byte(ttp++);
#else
                        s->state = *ttp++;
#endif
                        // tell the caller to advance
                        return 1;
                    } else {
                        ++ttp;
                    }
                }
                return 0;
            }
            inline static uint32_t accept(int state) {
                return entity_dfa_accept_table_xml[state];
            }
        };
    }
}
#endif