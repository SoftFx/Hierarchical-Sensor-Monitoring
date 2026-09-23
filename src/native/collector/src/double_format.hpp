// .NET shortest round-trip ("R") double text — the canonical payload contract
// (tests/conformance/README.md). Extracted from hsm_collector.cpp unchanged (#1426) so the
// prediction sensor's comment, built in disk_prediction.hpp, renders its speed exactly the way a
// payload value does; the number-format conformance fixture pins the behavior for both.
//
// std::to_chars produces the shortest digits, but its fixed/scientific choice differs from .NET
// (e.g. 1e5: to_chars "1e+05", .NET "100000"), so the digits are extracted from the scientific form
// and reassembled with .NET rules: fixed notation iff the decimal exponent is in [-4, 14],
// otherwise "dE±XX" with an uppercase 'E' and a sign-prefixed exponent of at least two digits.

#pragma once

#include <charconv>
#include <string>

namespace hsm
{
    namespace collector
    {
        inline std::string DoubleToInvariantString(double value)
        {
            char buffer[64];
            const auto result = std::to_chars(buffer, buffer + sizeof(buffer), value, std::chars_format::scientific);
            const std::string scientific(buffer, result.ptr);

            const bool negative = scientific[0] == '-';
            const auto exponent_marker = scientific.find('e');
            const int exponent = std::stoi(scientific.substr(exponent_marker + 1));

            std::string digits;
            for (size_t i = negative ? 1 : 0; i < exponent_marker; ++i)
                if (scientific[i] != '.')
                    digits.push_back(scientific[i]);

            std::string text;
            if (negative && value != 0.0)
                text.push_back('-');

            if (exponent >= -4 && exponent <= 14)
            {
                if (exponent < 0)
                {
                    text += "0.";
                    text.append(static_cast<size_t>(-exponent - 1), '0');
                    text += digits;
                }
                else if (static_cast<size_t>(exponent) + 1 >= digits.size())
                {
                    text += digits;
                    text.append(static_cast<size_t>(exponent) + 1 - digits.size(), '0');
                }
                else
                {
                    text += digits.substr(0, static_cast<size_t>(exponent) + 1);
                    text.push_back('.');
                    text += digits.substr(static_cast<size_t>(exponent) + 1);
                }
            }
            else
            {
                text += digits.substr(0, 1);
                if (digits.size() > 1)
                {
                    text.push_back('.');
                    text += digits.substr(1);
                }

                text.push_back('E');
                text.push_back(exponent < 0 ? '-' : '+');

                const auto magnitude = std::to_string(exponent < 0 ? -exponent : exponent);
                if (magnitude.size() < 2)
                    text.push_back('0');
                text += magnitude;
            }

            return text;
        }
    } // namespace collector
} // namespace hsm
