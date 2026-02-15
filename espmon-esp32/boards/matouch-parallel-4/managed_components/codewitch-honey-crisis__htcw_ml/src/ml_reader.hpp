#ifndef HTCW_ML_ML_READER_HPP
#define HTCW_ML_ML_READER_HPP
#include <string.h>
#include <io_lex_source.hpp>
// DFA table generated with a variant of Rolex: https://www.codeproject.com/Articles/5257489/Rolex-Unicode-Enabled-Lexer-Generator-in-Csharp
#include "ml_reader_fa.hpp"

namespace ml {
    enum struct ml_node_type {
        error_eof        = -5, // Unexpected EOF                             
        error_eref       = -4, // Invalid character or entity reference
        error_eclose     = -3, // Close tag does not match open tag (<Tag> .. </OtherTag>) 
        error_overflow   = -2, // Stack overflow (too deeply nested tags or too long element/attribute name) 
        error_syntax     = -1, // Syntax error (unexpected byte)             
        initial          =  0, // Start of document                          
        element          =  1, // Start of an element:   '<Tag ..'           
        content          =  2, // Element content                            
        element_end      =  3, // End of an element:     '.. />' or '</Tag>' 
        attribute        =  4, // Attribute:             'Name=..'           
        attribute_content=  5, // Attribute value                            
        attribute_end    =  6,
        comment          =  7, // Start of a comment
        comment_content  =  8, // Content of a comment                            
        comment_end      =  9, // End of a comment
        pi               =  10, // Start of a processing instruction          
        pi_content       =  11,// Content of a processing instruction
        pi_end           =  12,// End of a processing instruction            
        notation         =  13,// Start of notation '<!foo'
        notation_content =  14,// Notation content
        notation_end     =  15, //Notation end '>'
        eof              =  16,// End of document
    };
    class ml_reader_base {
    public:
        virtual int depth() const = 0;
        virtual char attribute_quote() const=0;
        virtual bool is_empty_element() const=0;
        virtual bool denormalize_script_elements() const=0;
        virtual void denormalize_script_elements(bool value)=0;
        virtual bool read()=0;
        virtual bool skip_to_end_element()=0;
        virtual bool skip_to_element_children()=0;
        virtual const char* value() const=0;
        virtual bool has_value() const=0;
        virtual bool has_content() const=0;
        virtual ml_node_type node_type() const=0;
    };
    template<size_t CaptureSize=1024,ml_entity_support EntitySupport=
#ifndef HTCW_ML_HTML_ENTITIES    
        ml_entity_support::xml_only
#else
        ml_entity_support::all
#endif
    > class ml_reader_ex final : public ml_reader_base {
        static_assert(CaptureSize>=64,"The capture size must be at least 64.");
        using ls_type = io::lex_source<CaptureSize>;
        using lex_type = helpers::entity_lexer<EntitySupport>;
        constexpr static const bool has_html_entities = 
#ifndef HTCW_ML_HTML_ENTITIES
        false;
#else
        EntitySupport==ml_entity_support::all;
#endif
        int m_state;
        int m_depth;
        ls_type m_source;
        int m_end_quote;
        int m_is_empty_elem;
        int m_denormalize_script_elements;
        char m_last_elem_name[CaptureSize];
        enum private_state {
            state_cdata = (int)ml_node_type::eof+1,
            state_content_unknown,
            state_pi_final,
            state_notation_final,
            state_attribute_value_part,
            state_attribute_value_final,
            state_comment_final,
            state_script_content,
            state_script_final
        };
        static bool until_left_bracket(uint32_t codepoint) {
            return '<'!=codepoint;
        }
        static int is_name_start_char(uint32_t codepoint,int nc=0) {
            return (codepoint>='A' && codepoint<='Z') ||
                (0==nc && codepoint==':') ||
                codepoint=='_' ||
                (codepoint>='a' && codepoint<='z') ||
                (codepoint>=0xC0 && codepoint<=0xD6) || 
                (codepoint>=0xD8 && codepoint<=0xF6) ||
                (codepoint>=0xF8 && codepoint<=0x2FF) ||
                (codepoint>=0x370 && codepoint<=0x37D) ||
                (codepoint>=0x37F && codepoint<=0x1FFF) ||
                (codepoint>=0x200C && codepoint<=0x200D) ||
                (codepoint>=0x2070 && codepoint<=0x218F) ||
                (codepoint>=0x2C00 && codepoint<=0x2FEF) ||
                (codepoint>=0x3001 && codepoint<=0xD7FF) ||
                (codepoint>=0xF900 && codepoint<=0xFDCF) ||
                (codepoint>=0xFDF0 && codepoint<=0xFFFD) ||
                (codepoint>=0x10000 && codepoint<=0xEFFFF);
        }
        static int is_name_char(uint32_t codepoint, int nc=0) {
            return is_name_start_char(codepoint,nc) ||
                codepoint=='-' ||
                codepoint=='.' ||
                (codepoint>='0' && codepoint<='9') ||
                codepoint==0xB7 ||
                (codepoint>=0x300 && codepoint<=0x36F) ||
                (codepoint>=0x203F && codepoint<=0x2040);
        }
        int read_name(int nc=0) {
            m_source.clear_capture();
            if(is_name_start_char(m_source.current(),nc)) {
                m_source.capture(m_source.current());
                if(!m_source.advance()) {
                    return 0;
                }
                while(is_name_char(m_source.current(),nc)) {
                    m_source.capture(m_source.current());
                    if(!m_source.advance())
                        break;
                }
                return 1;
            }
            return 0;
        }
        int read_content(uint32_t terminator) {
            m_is_empty_elem=0;
            int result = 0;
            while(m_source.capture_capacity()-m_source.capture_size()>5 && (terminator!=0 || !(ls_type::is_whitespace(m_source.current()))|| m_source.current()=='/' || m_source.current()=='>' ) && ((int32_t)terminator)!=m_source.current() && m_source.more()) {
                if(m_source.current()=='&') {
                    if(m_source.capture_capacity()-m_source.capture_size()<(lex_type::max_len*2)+1) {
                        // we'll need to flush to prevent a potential overflow
                        return 1;
                    }
                    uint32_t acc = lex_entity();
                    if(0!=acc) {
                        if(!m_source.capture(acc)) {
                            m_state=(int)ml_node_type::error_overflow;
                            return 1;    
                        }
                    } else {
                        m_state=(int)ml_node_type::error_eref;
                        return 1;
                    }
                } else {
                    if(!m_source.capture(m_source.current())) {
                        m_state=(int)ml_node_type::error_overflow;
                        return 1;
                    }
                    if(!m_source.advance()) {
                        return 1;
                    }
                }
                result = 1;
            }
            return result;
        }
        int parse_cdata_part() {
            //if(m_state!=state_cdata) return 0;
            m_state = state_cdata;
            while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.more()) {
                while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.current()!=']' && m_source.more()) {
                    m_source.capture(m_source.current());
                    m_source.advance();
                }
                if(m_source.current()==']') {
                    if(m_source.capture_capacity()-m_source.capture_size()<7) {
                        return 1;
                    }
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    if(m_source.current()!=']') {
                        m_source.capture(']');
                        continue;
                    }
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    if(m_source.current()!='>') {
                        m_source.capture(']');
                        m_source.capture(']');
                        continue;
                    }
                    m_source.advance();
                    m_state = (int)ml_node_type::content;
                    return 1;
                }
            }
            return 1;
        }
        
        int parse_pi_content() {
            m_state = (int)ml_node_type::pi_content;
            while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.more()) {
                while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.current()!='?' && m_source.more()) {
                    m_source.capture(m_source.current());
                    m_source.advance();
                }
                if(m_source.current()=='?') {
                    if(m_source.capture_capacity()-m_source.capture_size()<6) {
                        return 1;
                    }
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    if(m_source.current()!='>') {
                        m_source.capture('?');
                        continue;
                    }
                    m_state = state_pi_final;
                    return 1;
                }
            }
            return 1;
        }
        int parse_comment_content() {
            m_state = (int)ml_node_type::comment_content;
            while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.more()) {
                while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.current()!='-' && m_source.more()) {
                    m_source.capture(m_source.current());
                    m_source.advance();
                }
                if(m_source.current()=='-') {
                    if(m_source.capture_capacity()-m_source.capture_size()<7) {
                        return 1;
                    }
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    if(m_source.current()!='-') {
                        m_source.capture('-');
                        continue;
                    }
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    if(m_source.current()!='>') {
                        m_source.capture('-');
                        m_source.capture('-');
                        continue;
                    }
                    m_state = state_comment_final;
                    return 1;
                }
            }
            return 1;
        }
        int parse_script_content() {
            m_state = (int)state_script_content;
            while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.more()) {
                while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.current()!='<' && m_source.more()) {
                    m_source.capture(m_source.current());
                    m_source.advance();
                }
                if(m_source.current()=='<') {
                    if(m_source.capture_capacity()-m_source.capture_size()<13) {
                        return 1;
                    }
                }
                // </script>
                char buf[10];
                buf[0]='<';
                buf[1]='/';
                memcpy(buf+2,m_last_elem_name,6);
                buf[8]='>';
                buf[9]=0;
                char* pbuf=buf;
                while(*pbuf==m_source.current()) {
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    ++pbuf;
                }
                if(*pbuf!=0) {
                    char* pb = buf;
                    while(pb<pbuf) {
                        m_source.capture(*pb++);
                    }
                    continue;;
                }
                m_state = state_script_final;
                return 1;
                
            }
            return 1;
        }
        int parse_notation_content() {
            m_state = (int)ml_node_type::notation_content;
            while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.more()) {
                while(m_source.capture_capacity()-m_source.capture_size()>4 && m_source.current()!='>' && m_source.more()) {
                    m_source.capture(m_source.current());
                    m_source.advance();
                }
                if(m_source.current()=='>') {
                    m_state = state_notation_final;
                    return 1;
                }
            }
            return 1;
        }
        inline int has_error() const {
            return 0>m_state;
        }
        void set_error_if_none(int error) {
            if(-1<m_state) {
                m_state = error;
            }
        }
        
        int parse_attribute_value_or_attribute_name_or_content_or_end_element() {
            if(m_state==state_attribute_value_part) {
                // continuation
                m_source.clear_capture();    
                read_content(m_end_quote);
                if((m_end_quote==0&&ls_type::is_whitespace(m_source.current())) ||
                    m_end_quote==m_source.current()) {
                    m_source.advance();
                    m_state = state_attribute_value_final;
                }
                return 1;
            }
            if(0>m_state) {
                return 0;
            }
            if(m_state==(int)ml_node_type::eof) {
                m_state = (int)ml_node_type::error_eof;
                return 1;
            }
            m_source.clear_capture();
            if(m_source.current()=='=' || m_state==state_attribute_value_part) {
                if(m_state!=state_attribute_value_part) {
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                        return 1;
                    }
                    m_source.skip(ls_type::is_whitespace);
                    switch(m_source.current()) {
                        case '\"':
                        case '\'':
                            m_end_quote = m_source.current();
                            m_source.advance();
                            break;
                        default:
                            m_end_quote = 0;
                            m_source.skip(ls_type::is_whitespace);
                            break;
                    }
                    m_state = state_attribute_value_part;
                }
                read_content(m_end_quote);
                if((m_end_quote==0&&ls_type::is_whitespace(m_source.current())) || m_end_quote==m_source.current()) {
                    m_source.advance();
                    m_state = state_attribute_value_final;
                } else {
                    m_state = state_attribute_value_part;
                }

                return 1;
            }
            m_source.skip(ls_type::is_whitespace);
            return parse_attribute_name_or_content_or_end_element();
        }
        int parse_attribute_name_or_content_or_end_element() {
            if(0>m_state) {
                return 0;
            }
            if(m_state==(int)ml_node_type::eof) {
                m_state = (int)ml_node_type::error_eof;
                return 1;
            }
            m_source.clear_capture();
            
            switch(m_source.current()) {
                case '/':
                    m_is_empty_elem=1;
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::error_eof;
                    }
                    if(m_source.current()!='>') {
                        m_state= (int)ml_node_type::error_syntax;
                    }
                    if(!m_source.capture_fill(m_last_elem_name)) {
                        m_state = (int)ml_node_type::error_overflow;
                    }
                    m_state = (int)ml_node_type::element_end;
                    return 1;
                case '>':
                    m_is_empty_elem=0;
                    if(!m_source.advance()) {
                        m_state = (int)ml_node_type::eof;
                        return 1;
                    } else {
                        return parse_element_or_comment_or_pi_or_notation_or_content_or_end_element_or_eof();
                    }
                    break;
            }    
            if(!read_name()) {
                m_state = (int)ml_node_type::error_syntax;
                return 1;
            }
            if(m_source.current()!='=' && m_source.current()!='/' && m_source.current()!='>' && !m_source.is_whitespace(m_source.current())) {
                m_state = (int)ml_node_type::error_syntax;
                return 1;
            }
            m_source.skip(ls_type::is_whitespace);
            m_state = (int)ml_node_type::attribute;
            return 1;
        }

        int parse_element_or_comment_or_pi_or_notation_or_content_or_end_element_or_eof() {
            if(0>m_state || m_state==(int)ml_node_type::eof) {
                return 0;
            }
            if(m_denormalize_script_elements) {
                // script tags DO NOT CONFORM. We have to special case them
                ml_node_type nt = node_type();
                // some platforms don't have stricmp
                if((nt==ml_node_type::element||
                    nt==ml_node_type::attribute_end || 
                    nt==ml_node_type::attribute) &&
                    ((m_last_elem_name[0]=='s' && 0==strcmp(m_last_elem_name+1,"cript"))|| 
                    (m_last_elem_name[0]=='S' && 0==strcmp(m_last_elem_name+1,"CRIPT")))) {
                    return parse_script_content();
                }
            }
            m_source.clear_capture();
            if(read_content('<')) {
                m_state = (int)ml_node_type::content;
                return 1;
            }
            if('<'==m_source.current()) {
                if(!m_source.advance()) {
                    m_state = (int)ml_node_type::error_eof;
                    return 1;
                }
                switch(m_source.current()) {
                    // either notation, cdata or comment
                    case '!':
                        if(!m_source.advance()) {
                            m_state = (int)ml_node_type::error_eof;
                            return 1;
                        }
                        // <![CDATA[...]]>
                        if('['==m_source.current()) {
                            if(!m_source.advance()) {
                                m_state = (int)ml_node_type::error_eof;
                                return 1;
                            }   
                            if(!read_name() || has_error()) {
                                set_error_if_none((int)ml_node_type::error_syntax);
                                return 1;
                            }
                            if(0!=strcmp(m_source.capture_buffer(),"CDATA")) {
                                m_state = (int)ml_node_type::error_syntax;
                                return 1;
                            }
                            if(m_source.current()!='[') {
                                m_state = (int)ml_node_type::error_syntax;
                                return 1;
                            }
                            if(!m_source.advance()) {
                                m_state = (int)ml_node_type::error_eof;
                                return 1;
                            }
                            m_source.clear_capture();
                            m_state = state_cdata;
                            return parse_cdata_part();
                        } else if('-'==m_source.current()) {
                            if(!m_source.advance()) {
                                m_state = (int)ml_node_type::error_eof;
                                return 1;
                            }
                            if(m_source.current()!='-') {
                                m_state = (int)ml_node_type::error_syntax;
                                return 1;
                            }
                            if(!m_source.advance()) {
                                m_state = (int)ml_node_type::error_eof;
                                return 1;
                            }
                            m_state = (int)ml_node_type::comment;
                            return 1;
                        } else if(!read_name() || has_error()) {
                            set_error_if_none((int)ml_node_type::error_syntax);
                            return 1;
                        }
                        if(ls_type::is_whitespace(m_source.current())) {
                            m_source.skip(ls_type::is_whitespace);
                            m_state = (int)ml_node_type::notation;
                        } else if('>'!=m_source.current()) {
                            m_state = (int)ml_node_type::error_syntax;
                        } else {
                            m_state = (int)ml_node_type::notation;
                        }
                        return 1;
                    case '?':
                        // processing instruction
                        if(!m_source.advance()) {
                            m_state = (int)ml_node_type::error_eof;
                            return 1;
                        }
                        if(!read_name() || has_error()) {
                            set_error_if_none((int)ml_node_type::error_syntax);
                            return 1;
                        }
                        if(ls_type::is_whitespace(m_source.current())) {
                            m_source.skip(ls_type::is_whitespace);
                        } else if('?'!=m_source.current()) {
                            m_state = (int)ml_node_type::error_syntax;
                        }
                        m_state = (int)ml_node_type::pi;
                        return 1;
                    case '/':
                        // end element </foo:bar>
                        if(!m_source.advance()) {
                            m_state = (int)ml_node_type::error_eof;
                            return 1;
                        }
                        if(!read_name() || has_error()) {
                            set_error_if_none((int)ml_node_type::error_syntax);
                            return 1;
                        }
                        if(m_source.current()!='>') {
                            m_state = (int)ml_node_type::error_syntax;
                            return 1;
                        }
                        m_state=(int)ml_node_type::element_end;
                        return 1;
                    default:
                        // <foo:bar
                        if(!read_name() || has_error()) {
                            set_error_if_none((int)ml_node_type::error_syntax);
                            return 1;
                        }
                        strcpy(m_last_elem_name,m_source.const_capture_buffer());
                        if(ls_type::is_whitespace(m_source.current())) {
                            m_source.skip(ls_type::is_whitespace);
                        }
                        m_state = (int)ml_node_type::element;
                        return 1;
                }  
            } 
            if(m_source.eof()) {
                m_state = (int)ml_node_type::eof;
                return true;
            }
            // shouldn't get here?
            return 0;
        }
    public:
        constexpr static const size_t capture_size = CaptureSize;
        ml_reader_ex(io::stream* in) : m_state(0),m_source(in)  {
            m_denormalize_script_elements = 1;
            *m_last_elem_name = 0;
            m_depth  = 0;
        }
        ml_reader_ex() : m_state(0),m_source(nullptr)  {
            m_denormalize_script_elements = 1;
            *m_last_elem_name = 0;
            m_depth = 0;
        }
        void set(io::stream& stream) {
            if(stream.caps().read==0) {
                return;
            }
            m_source = &stream;
            *m_last_elem_name = 0;
            m_depth = 0;
        }
        virtual int depth() const override {
            return m_depth;
        }
        virtual char attribute_quote() const override {
            switch(node_type()) {
                case ml_node_type::attribute_content:
                case ml_node_type::attribute_end:
                    return m_end_quote;
                default:
                    break;
            }
            return 0;
        }
        virtual bool is_empty_element() const override {
            return 0!=m_is_empty_elem && (node_type()==ml_node_type::element_end);
        }
        virtual bool denormalize_script_elements() const override {
            return m_denormalize_script_elements;
        }
        virtual void denormalize_script_elements(bool value) override {
            m_denormalize_script_elements = value;
        }
        virtual bool read() override {
            m_source.ensure_started();
            m_source.clear_capture();
            do {
                switch(m_state) {
                    case (int)ml_node_type::initial:
                    case (int)ml_node_type::content:
                    case state_content_unknown:
                        return parse_element_or_comment_or_pi_or_notation_or_content_or_end_element_or_eof();
                    case state_cdata:
                        return parse_cdata_part();
                    case state_comment_final:
                        m_state = (int)ml_node_type::comment_end;
                        return 1;
                    case state_pi_final:
                        m_state = (int)ml_node_type::pi_end;
                        return 1;
                    case state_script_final:
                        if(!m_source.capture_fill(m_last_elem_name)) {
                            m_state = (int)ml_node_type::error_overflow;
                            return 1;
                        }
                        m_state = (int)ml_node_type::element_end;
                        return 1;
                    case state_script_content:
                        return parse_script_content();
                    case state_attribute_value_part:
                        return parse_attribute_value_or_attribute_name_or_content_or_end_element();
                    case state_notation_final:
                        m_state = (int)ml_node_type::notation_end;
                        return 1;
                    case state_attribute_value_final:
                        m_state = (int)ml_node_type::attribute_end;
                        return 1;
                    case (int)ml_node_type::element:                        
                        ++m_depth;
                        return parse_attribute_name_or_content_or_end_element();
                    case (int)ml_node_type::attribute_end:
                    case (int)ml_node_type::attribute:
                        return parse_attribute_value_or_attribute_name_or_content_or_end_element();
                    case (int)ml_node_type::pi:
                        return parse_pi_content();
                    case (int)ml_node_type::pi_content:
                        return parse_pi_content();
                    case (int)ml_node_type::comment:
                        return parse_comment_content();
                    case (int)ml_node_type::comment_content:
                        return parse_comment_content();
                    
                    case (int)ml_node_type::notation:
                        return parse_notation_content();
                    case (int)ml_node_type::notation_content:
                        return parse_notation_content();
                    case (int)ml_node_type::element_end:
                        --m_depth;
                        [[fallthrough]];
                    case (int)ml_node_type::pi_end:
                    case (int)ml_node_type::notation_end:
                    case (int)ml_node_type::comment_end:
                        if(m_source.current()!='>') {
                            m_state = (int)ml_node_type::error_syntax;
                        }
                        m_source.advance();
                        m_state = state_content_unknown;
                        break;
                }
            } while(m_state==state_content_unknown) ;
            return false;
        }
        virtual bool skip_to_end_element() override {
            if(node_type()==ml_node_type::element_end) {
                return true;
            }
            int i = 0;
            while(read()) {
                if(node_type()==ml_node_type::element) {
                    ++i;
                } else if(node_type()==ml_node_type::element_end) {
                    --i;
                    if(0>i) {
                        break;
                    }
                }
            }
            return node_type()==ml_node_type::element_end;
        }
        virtual bool skip_to_element_children() override {
            ml_node_type nt = node_type();
            switch(nt) {
                case ml_node_type::element_end:
                case ml_node_type::comment:
                case ml_node_type::comment_content:
                case ml_node_type::comment_end:
                case ml_node_type::content:
                case ml_node_type::eof:
                case ml_node_type::error_eclose:
                case ml_node_type::error_eof:
                case ml_node_type::error_eref:
                case ml_node_type::error_overflow:
                case ml_node_type::error_syntax:
                case ml_node_type::pi:
                case ml_node_type::pi_content:
                case ml_node_type::pi_end:
                case ml_node_type::initial:
                case ml_node_type::notation:
                case ml_node_type::notation_content:
                case ml_node_type::notation_end:
                    return false;
                case ml_node_type::attribute:
                case ml_node_type::attribute_end:
                case ml_node_type::attribute_content:
                case ml_node_type::element:
                    break;
            }
            while(nt==ml_node_type::element||nt==ml_node_type::attribute||nt==ml_node_type::attribute_content||nt==ml_node_type::attribute_end) {
                if(!read()) {
                    return false;
                }
                nt=node_type();
                if(nt==ml_node_type::element_end) {
                    return false;
                }
            }
            return true;
        }
        virtual const char* value() const override {
            switch(node_type()) {
                case ml_node_type::element:
                case ml_node_type::element_end:
                case ml_node_type::notation:
                case ml_node_type::attribute:
                case ml_node_type::pi:
                case ml_node_type::content:
                case ml_node_type::notation_content:
                case ml_node_type::comment_content:
                case ml_node_type::attribute_content:
                case ml_node_type::pi_content:
                    return m_source.const_capture_buffer();
                default:
                    break;
            }
            return nullptr;
        }
        virtual ml_node_type node_type() const override {
            switch(m_state) {
            case state_cdata:
            case state_script_content:
            case state_script_final:
                return ml_node_type::content;
            case state_comment_final:
                return ml_node_type::comment_content;
            case state_pi_final:
                return ml_node_type::pi_content;
            case state_notation_final:
                return ml_node_type::notation_content;
            case state_attribute_value_part:
            case state_attribute_value_final:
                return ml_node_type::attribute_content;
            default:
                return (ml_node_type)m_state;
            }
        }
        virtual bool has_value() const override {
            switch(node_type()) {
                case ml_node_type::element:
                case ml_node_type::element_end:
                case ml_node_type::notation:
                case ml_node_type::attribute:
                case ml_node_type::pi:
                case ml_node_type::content:
                case ml_node_type::notation_content:
                case ml_node_type::comment_content:
                case ml_node_type::attribute_content:
                case ml_node_type::pi_content:
                    return true;
                default:
                    break;
            }
            return false;
        }
        virtual bool has_content() const override {
            switch(node_type()) {
                case ml_node_type::content:
                case ml_node_type::notation_content:
                case ml_node_type::comment_content:
                case ml_node_type::attribute_content:
                case ml_node_type::pi_content:
                    return true;
                default:
                    break;
            }
            return false;
        }
    private:
        int lex_entity() {
            uint32_t buf[lex_type::max_len];
            helpers::entity_lex_state s;
            s.state = 0;
            s.last_match_state = 0;
            s.accept = lex_type::none;
            s.buf = buf;
            s.buflen = 0;
            s.num = 0;
            s.flags = 0;
            while(0!=lex_type::lex_entity_part(m_source.current(),&s)) {
                if(!m_source.advance()) {
                    m_state = (int)ml_node_type::error_eof;
                    return 0;
                }
                switch(s.flags) {
                    case 0:
                        switch(m_source.current()) {
                            case '#':
                                s.flags = 1;
                                break;
                            case 'x':
                            case 'X':
                                s.flags = 2;
                                break;;
                        }
                        break;
                    case 1:
                        if(m_source.current()>='0'&&m_source.current()<='9') {
                            s.num*=10;
                            s.num+=m_source.current()-'0';
                        } else {
                            s.flags = 3;
                        }
                        break;
                    case 2:
                        if(m_source.current()>='0'&&m_source.current()<='9') {
                            s.num*=16;
                            s.num+=m_source.current()-'0';
                        } else if(m_source.current()>='A'&&m_source.current()<='F') {
                            s.num*=16;
                            s.num+=(m_source.current()-'A')+10;
                        } else if(m_source.current()>='a'&&m_source.current()<='f') {
                            s.num*=16;
                            s.num+=(m_source.current()-'a')+10;
                        } else {
                            s.flags = 3;
                        }
                        break;
                }
            }
            unsigned int acc = lex_type::accept(s.state);
            if(acc<2) {
                acc = s.num;
            } 
            if(acc!=lex_type::none) {
                return acc;
            } 
        
            if(0!=s.buflen) {
                // MUST HAVE THE FREE BUFFER TO DO THIS!
                // assert(m_source->capture_capacity()-m_source->capture_size()>=(s->buflen*2)+1);
                for(unsigned int i = 0;i<s.buflen-1;++i) {
                    if(!m_source.capture(s.buf[i])) {
                        m_state = (int)ml_node_type::error_overflow;
                    }
                }
                return s.buf[s.buflen-1];
            }
            return 0;
        }
        
    };
    using ml_reader = ml_reader_ex<1024>;
}
#endif