#include "agent/config.hpp"

#include <cctype>
#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace hsm::agent
{
    namespace
    {
        // --- A minimal, dependency-free JSON value + recursive-descent parser ------------------
        //
        // The collector exposes no general JSON parser (it hand-builds the wire format), and the
        // epic mandates minimal dependencies, so the agent reads its small config with this. It
        // supports the full JSON grammar the schema needs: objects, arrays, strings (with escapes,
        // incl. \uXXXX), numbers, true/false/null. Errors carry a byte offset for a clear message.

        struct JsonValue
        {
            enum class Type
            {
                Null,
                Bool,
                Number,
                String,
                Array,
                Object
            };

            Type type = Type::Null;
            bool boolean = false;
            double number = 0.0;
            std::string text;
            std::vector<JsonValue> elements;
            std::vector<std::pair<std::string, JsonValue>> members;

            const JsonValue* Find(const std::string& key) const
            {
                for (const auto& member : members)
                    if (member.first == key)
                        return &member.second;
                return nullptr;
            }
        };

        class JsonParser
        {
        public:
            JsonParser(const std::string& text, std::string& error)
                : text_(text), error_(error)
            {
            }

            bool Parse(JsonValue& out)
            {
                SkipWhitespace();
                if (!ParseValue(out))
                    return false;
                SkipWhitespace();
                if (pos_ != text_.size())
                    return Fail("trailing characters after JSON document");
                return true;
            }

        private:
            bool Fail(const std::string& message)
            {
                std::ostringstream stream;
                stream << message << " (at offset " << pos_ << ")";
                error_ = stream.str();
                return false;
            }

            void SkipWhitespace()
            {
                while (pos_ < text_.size())
                {
                    const char c = text_[pos_];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        ++pos_;
                    else
                        break;
                }
            }

            bool ParseValue(JsonValue& out)
            {
                if (pos_ >= text_.size())
                    return Fail("unexpected end of input");

                const char c = text_[pos_];
                switch (c)
                {
                case '{':
                    return ParseObject(out);
                case '[':
                    return ParseArray(out);
                case '"':
                    out.type = JsonValue::Type::String;
                    return ParseString(out.text);
                case 't':
                case 'f':
                    return ParseBool(out);
                case 'n':
                    return ParseNull(out);
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber(out);
                    return Fail("unexpected character");
                }
            }

            bool ParseObject(JsonValue& out)
            {
                out.type = JsonValue::Type::Object;
                ++pos_; // consume '{'
                SkipWhitespace();
                if (pos_ < text_.size() && text_[pos_] == '}')
                {
                    ++pos_;
                    return true;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (pos_ >= text_.size() || text_[pos_] != '"')
                        return Fail("expected string key in object");

                    std::string key;
                    if (!ParseString(key))
                        return false;

                    SkipWhitespace();
                    if (pos_ >= text_.size() || text_[pos_] != ':')
                        return Fail("expected ':' after object key");
                    ++pos_;

                    SkipWhitespace();
                    JsonValue value;
                    if (!ParseValue(value))
                        return false;
                    out.members.emplace_back(std::move(key), std::move(value));

                    SkipWhitespace();
                    if (pos_ >= text_.size())
                        return Fail("unterminated object");
                    if (text_[pos_] == ',')
                    {
                        ++pos_;
                        continue;
                    }
                    if (text_[pos_] == '}')
                    {
                        ++pos_;
                        return true;
                    }
                    return Fail("expected ',' or '}' in object");
                }
            }

            bool ParseArray(JsonValue& out)
            {
                out.type = JsonValue::Type::Array;
                ++pos_; // consume '['
                SkipWhitespace();
                if (pos_ < text_.size() && text_[pos_] == ']')
                {
                    ++pos_;
                    return true;
                }

                while (true)
                {
                    SkipWhitespace();
                    JsonValue value;
                    if (!ParseValue(value))
                        return false;
                    out.elements.push_back(std::move(value));

                    SkipWhitespace();
                    if (pos_ >= text_.size())
                        return Fail("unterminated array");
                    if (text_[pos_] == ',')
                    {
                        ++pos_;
                        continue;
                    }
                    if (text_[pos_] == ']')
                    {
                        ++pos_;
                        return true;
                    }
                    return Fail("expected ',' or ']' in array");
                }
            }

            bool ParseString(std::string& out)
            {
                ++pos_; // consume opening quote
                out.clear();
                while (pos_ < text_.size())
                {
                    const char c = text_[pos_++];
                    if (c == '"')
                        return true;
                    if (c == '\\')
                    {
                        if (pos_ >= text_.size())
                            return Fail("unterminated escape in string");
                        const char esc = text_[pos_++];
                        switch (esc)
                        {
                        case '"':
                            out.push_back('"');
                            break;
                        case '\\':
                            out.push_back('\\');
                            break;
                        case '/':
                            out.push_back('/');
                            break;
                        case 'b':
                            out.push_back('\b');
                            break;
                        case 'f':
                            out.push_back('\f');
                            break;
                        case 'n':
                            out.push_back('\n');
                            break;
                        case 'r':
                            out.push_back('\r');
                            break;
                        case 't':
                            out.push_back('\t');
                            break;
                        case 'u':
                            if (!ParseUnicodeEscape(out))
                                return false;
                            break;
                        default:
                            return Fail("invalid escape sequence in string");
                        }
                    }
                    else
                    {
                        out.push_back(c);
                    }
                }
                return Fail("unterminated string");
            }

            bool ParseUnicodeEscape(std::string& out)
            {
                unsigned int code = 0;
                if (!ReadHex4(code))
                    return false;

                // Surrogate pair: combine a high surrogate with a following \uDC00-\uDFFF.
                if (code >= 0xD800 && code <= 0xDBFF)
                {
                    if (pos_ + 1 < text_.size() && text_[pos_] == '\\' && text_[pos_ + 1] == 'u')
                    {
                        pos_ += 2;
                        unsigned int low = 0;
                        if (!ReadHex4(low))
                            return false;
                        if (low < 0xDC00 || low > 0xDFFF)
                            return Fail("invalid low surrogate in \\u escape");
                        code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
                    }
                }

                AppendUtf8(code, out);
                return true;
            }

            bool ReadHex4(unsigned int& out)
            {
                if (pos_ + 4 > text_.size())
                    return Fail("truncated \\u escape");
                out = 0;
                for (int i = 0; i < 4; ++i)
                {
                    const char c = text_[pos_++];
                    out <<= 4;
                    if (c >= '0' && c <= '9')
                        out |= static_cast<unsigned int>(c - '0');
                    else if (c >= 'a' && c <= 'f')
                        out |= static_cast<unsigned int>(c - 'a' + 10);
                    else if (c >= 'A' && c <= 'F')
                        out |= static_cast<unsigned int>(c - 'A' + 10);
                    else
                        return Fail("invalid hex digit in \\u escape");
                }
                return true;
            }

            static void AppendUtf8(unsigned int code, std::string& out)
            {
                if (code <= 0x7F)
                {
                    out.push_back(static_cast<char>(code));
                }
                else if (code <= 0x7FF)
                {
                    out.push_back(static_cast<char>(0xC0 | (code >> 6)));
                    out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
                }
                else if (code <= 0xFFFF)
                {
                    out.push_back(static_cast<char>(0xE0 | (code >> 12)));
                    out.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
                    out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
                }
                else
                {
                    out.push_back(static_cast<char>(0xF0 | (code >> 18)));
                    out.push_back(static_cast<char>(0x80 | ((code >> 12) & 0x3F)));
                    out.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
                    out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
                }
            }

            bool ParseBool(JsonValue& out)
            {
                if (text_.compare(pos_, 4, "true") == 0)
                {
                    pos_ += 4;
                    out.type = JsonValue::Type::Bool;
                    out.boolean = true;
                    return true;
                }
                if (text_.compare(pos_, 5, "false") == 0)
                {
                    pos_ += 5;
                    out.type = JsonValue::Type::Bool;
                    out.boolean = false;
                    return true;
                }
                return Fail("invalid literal");
            }

            bool ParseNull(JsonValue& out)
            {
                if (text_.compare(pos_, 4, "null") == 0)
                {
                    pos_ += 4;
                    out.type = JsonValue::Type::Null;
                    return true;
                }
                return Fail("invalid literal");
            }

            bool ParseNumber(JsonValue& out)
            {
                const std::size_t start = pos_;
                if (pos_ < text_.size() && text_[pos_] == '-')
                    ++pos_;
                while (pos_ < text_.size())
                {
                    const char c = text_[pos_];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                        ++pos_;
                    else
                        break;
                }
                const std::string token = text_.substr(start, pos_ - start);
                try
                {
                    out.number = std::stod(token);
                }
                catch (...)
                {
                    return Fail("invalid number");
                }
                out.type = JsonValue::Type::Number;
                return true;
            }

            const std::string& text_;
            std::string& error_;
            std::size_t pos_ = 0;
        };

        // --- Mapping helpers --------------------------------------------------------------------

        bool ReadString(const JsonValue& object, const std::string& key, std::string& out, std::string& error)
        {
            const JsonValue* value = object.Find(key);
            if (value == nullptr)
                return true; // absent → keep default
            if (value->type != JsonValue::Type::String)
            {
                error = "field '" + key + "' must be a string";
                return false;
            }
            out = value->text;
            return true;
        }

        bool ReadBool(const JsonValue& object, const std::string& key, bool& out, std::string& error)
        {
            const JsonValue* value = object.Find(key);
            if (value == nullptr)
                return true;
            if (value->type != JsonValue::Type::Bool)
            {
                error = "field '" + key + "' must be a boolean";
                return false;
            }
            out = value->boolean;
            return true;
        }

        bool ReadInt(const JsonValue& object, const std::string& key, int& out, std::string& error)
        {
            const JsonValue* value = object.Find(key);
            if (value == nullptr)
                return true;
            if (value->type != JsonValue::Type::Number)
            {
                error = "field '" + key + "' must be a number";
                return false;
            }
            out = static_cast<int>(value->number);
            return true;
        }

    } // namespace

    bool AgentConfig::ComputerNameIsAuto() const
    {
        return computer_name.empty() || computer_name == "auto";
    }

    bool ParseAgentConfig(const std::string& json_text, AgentConfig& out, std::string& error)
    {
        error.clear();
        out = AgentConfig{};

        JsonValue root;
        if (!JsonParser(json_text, error).Parse(root))
            return false;
        if (root.type != JsonValue::Type::Object)
        {
            error = "config root must be a JSON object";
            return false;
        }

        if (const JsonValue* server = root.Find("server"))
        {
            if (server->type != JsonValue::Type::Object)
            {
                error = "'server' must be an object";
                return false;
            }
            if (!ReadString(*server, "address", out.server_address, error) || !ReadInt(*server, "port", out.port, error) || !ReadString(*server, "accessKey", out.access_key, error) || !ReadBool(*server, "allowUntrustedCertificate", out.allow_untrusted_certificate, error))
                return false;
        }

        if (const JsonValue* identity = root.Find("identity"))
        {
            if (identity->type != JsonValue::Type::Object)
            {
                error = "'identity' must be an object";
                return false;
            }
            if (!ReadString(*identity, "computerName", out.computer_name, error) || !ReadString(*identity, "module", out.module, error))
                return false;
        }

        if (const JsonValue* sensors = root.Find("sensors"))
        {
            if (sensors->type != JsonValue::Type::Object)
            {
                error = "'sensors' must be an object";
                return false;
            }
            if (!ReadBool(*sensors, "computer", out.sensors_computer, error) || !ReadBool(*sensors, "system", out.sensors_system, error) || !ReadBool(*sensors, "disk", out.sensors_disk, error) || !ReadBool(*sensors, "network", out.sensors_network, error) || !ReadBool(*sensors, "module", out.sensors_module, error) || !ReadBool(*sensors, "process", out.sensors_process, error))
                return false;
        }

        if (const JsonValue* periods = root.Find("periods"))
        {
            if (periods->type != JsonValue::Type::Object)
            {
                error = "'periods' must be an object";
                return false;
            }
            if (!ReadInt(*periods, "collectMs", out.collect_period_ms, error))
                return false;
        }

        if (!ReadString(root, "productVersion", out.product_version, error))
            return false;

        // Required-field validation (epic: blank key/address must refuse to start).
        if (out.server_address.empty())
        {
            error = "config: 'server.address' is required and must not be blank";
            return false;
        }
        if (out.access_key.empty())
        {
            error = "config: 'server.accessKey' is required and must not be blank";
            return false;
        }
        if (out.port <= 0 || out.port > 65535)
        {
            error = "config: 'server.port' must be between 1 and 65535";
            return false;
        }

        return true;
    }

    bool LoadAgentConfig(const std::string& path, AgentConfig& out, std::string& error)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream)
        {
            error = "cannot open config file: " + path;
            return false;
        }

        std::ostringstream buffer;
        buffer << stream.rdbuf();
        return ParseAgentConfig(buffer.str(), out, error);
    }
} // namespace hsm::agent
