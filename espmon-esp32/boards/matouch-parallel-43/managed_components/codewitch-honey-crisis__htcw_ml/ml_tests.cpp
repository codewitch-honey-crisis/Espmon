#include "ml_reader.hpp"
#include <catch2/catch_test_macros.hpp>
#include <cstring>
#include <string>

using namespace ml;

const char *node_str(ml_node_type node_type) {
  switch (node_type) {
  case ml_node_type::error_eof:
    return "ERROR_EOF";
  case ml_node_type::error_eref:
    return "ERROR_EREF";
  case ml_node_type::error_eclose:
    return "ERROR_ECLOSE";
  case ml_node_type::error_overflow:
    return "ERROR_OVERFLOW";
  case ml_node_type::error_syntax:
    return "ERROR_SYNTAX";
  case ml_node_type::initial:
    return "INITIAL";
  case ml_node_type::element:
    return "ELEMENT";
  case ml_node_type::content:
    return "CONTENT";
  case ml_node_type::element_end:
    return "ELEMENT_END";
  case ml_node_type::attribute:
    return "ATTRIBUTE";
  case ml_node_type::attribute_content:
    return "ATTRIBUTE_CONTENT";
  case ml_node_type::attribute_end:
    return "ATTRIBUTE_END";
  case ml_node_type::comment:
    return "COMMENT";
  case ml_node_type::comment_content:
    return "COMMENT_CONTENT";
  case ml_node_type::comment_end:
    return "COMMENT_END";
  case ml_node_type::pi:
    return "PI";
  case ml_node_type::pi_content:
    return "PI_CONTENT";
  case ml_node_type::pi_end:
    return "PI_END";
  case ml_node_type::notation:
    return "NOTATION";
  case ml_node_type::notation_content:
    return "NOTATION_CONTENT";
  case ml_node_type::notation_end:
    return "NOTATION_END";
  case ml_node_type::eof:
    return "EOF";
  default:
    return "UNKNOWN";
  }
}

struct ParseTestcase {
  ml::ml_node_type expected_node_type;
  const char *expected_node_content;
};

static void
assert_node_sequence(const char *xml,
                     std::initializer_list<ParseTestcase> expected) {
  INFO("TEST INPUT: " << xml);
  io::const_buffer_stream s(reinterpret_cast<const uint8_t*>(xml), strlen(xml));
  ml::ml_reader_ex<64> r;
  r.set(s);

  auto testcase = expected.begin();
  for (; testcase != expected.end(); ++testcase) {
    REQUIRE(r.read());
    auto expected_node_type = node_str(testcase->expected_node_type);
    CAPTURE(expected_node_type);
    auto node_type = node_str(r.node_type());
    CAPTURE(node_type);
    REQUIRE(node_type == expected_node_type);
    if (testcase->expected_node_content) {
      CAPTURE(testcase->expected_node_content);
      REQUIRE(r.value() != nullptr);
      auto node_content = std::string(r.value());
      CAPTURE(node_content);
      REQUIRE(node_content == testcase->expected_node_content);
    }
  }
}

TEST_CASE("comment->element") {
  SECTION("cuddled with element") {
    assert_node_sequence("<!--x--><svg/>",
                         {
                             {ml::ml_node_type::comment, nullptr},
                             {ml::ml_node_type::comment_content, "x"},
                             {ml::ml_node_type::comment_end, nullptr},
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::element_end, nullptr},
                         });
  }

  SECTION("space separated") {
    assert_node_sequence("<!--x--> <svg/>",
                         {
                             {ml::ml_node_type::comment, nullptr},
                             {ml::ml_node_type::comment_content, "x"},
                             {ml::ml_node_type::comment_end, nullptr},
                             {ml::ml_node_type::content, " "},
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::element_end, nullptr},
                         });
  }

  SECTION("multiple cuddled with element") {
    assert_node_sequence("<!--a--><!--b--><svg/>",
                         {
                             {ml::ml_node_type::comment, nullptr},
                             {ml::ml_node_type::comment_content, "a"},
                             {ml::ml_node_type::comment_end, nullptr},
                             {ml::ml_node_type::comment, nullptr},
                             {ml::ml_node_type::comment_content, "b"},
                             {ml::ml_node_type::comment_end, nullptr},
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::element_end, nullptr},
                         });
  }
}

TEST_CASE("PI->element") {
  assert_node_sequence("<?pi x=\"1\"?><svg/>",
                       {
                           {ml::ml_node_type::pi, "pi"},
                           {ml::ml_node_type::pi_content, "x=\"1\""},
                           {ml::ml_node_type::pi_end, nullptr},
                           {ml::ml_node_type::element, "svg"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("DOCTYPE->element") {
  assert_node_sequence("<!DOCTYPE svg><svg/>",
                       {
                           {ml::ml_node_type::notation, "DOCTYPE"},
                           {ml::ml_node_type::notation_content, "svg"},
                           {ml::ml_node_type::notation_end, nullptr},
                           {ml::ml_node_type::element, "svg"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("CDATA->element") {
  assert_node_sequence("<![CDATA[x]]><svg/>",
                       {
                           {ml::ml_node_type::content, "x"},
                           {ml::ml_node_type::element, "svg"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("element->element") {
  assert_node_sequence("<g/><path/>",
                       {
                           {ml::ml_node_type::element, "g"},
                           {ml::ml_node_type::element_end, nullptr},
                           {ml::ml_node_type::element, "path"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("closing tag->element") {
  assert_node_sequence("</g><path/>",
                       {
                           {ml::ml_node_type::element_end, "g"},
                           {ml::ml_node_type::element, "path"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("HTML entity->element") {
  assert_node_sequence("&amp;<svg/>",
                       {
                           {ml::ml_node_type::content, "&"},
                           {ml::ml_node_type::element, "svg"},
                           {ml::ml_node_type::element_end, nullptr},
                       });
}

TEST_CASE("tag parsing") {
  SECTION("tag open/close") {
    assert_node_sequence("<svg></svg>",
                         {
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::element_end, "svg"},
                         });
  }

  SECTION("element") {
    assert_node_sequence("<svg/>", {
                                       {ml::ml_node_type::element, "svg"},
                                       {ml::ml_node_type::element_end, nullptr},
                                   });
  }
}

TEST_CASE("attrribute parsing") {
  SECTION("single") {
    assert_node_sequence("<svg width=\"24\"/>",
                         {
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::attribute, "width"},
                             {ml::ml_node_type::attribute_content, "24"},
                             {ml::ml_node_type::attribute_end, nullptr},
                             {ml::ml_node_type::element_end, nullptr},
                         });
  }

  SECTION("multiple") {
    assert_node_sequence("<svg width=\"24\" height=\"24\"/>",
                         {
                             {ml::ml_node_type::element, "svg"},
                             {ml::ml_node_type::attribute, "width"},
                             {ml::ml_node_type::attribute_content, "24"},
                             {ml::ml_node_type::attribute_end, nullptr},
                             {ml::ml_node_type::attribute, "height"},
                             {ml::ml_node_type::attribute_content, "24"},
                             {ml::ml_node_type::attribute_end, nullptr},
                             {ml::ml_node_type::element_end, nullptr},
                         });
  }
}
