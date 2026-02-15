using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace HWKit
{
    public struct HardwareInfoLineInfo
    {
        public long Position { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Source { get; set; }

        public HardwareInfoLineInfo(long position, int line, int column, string source)
        {
            Position = position;
            Line = line;
            Column = column;
            Source = source;
        }
        private static string Trim(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            if (str.Length > 23)
            {
                return $"{str.Substring(0, 10)}...{str.Substring(str.Length - 10)}";
            }
            return str;
        }
        public override string ToString()
        {
            return $"line {Line}, column {Column}, position {Position} in {Trim(Source)}";
        }
    }
    public partial class HardwareInfoParseException : Exception
    {
        public HardwareInfoLineInfo Location { get; private set; }
        public HardwareInfoParseException(string message, HardwareInfoLineInfo location) : base(message)
        {
            Location = location;
        }
        public override string ToString()
        {
            return $"{Message} at {Location}";
        }
    }
    public struct HardwareInfoFunction : IEquatable<HardwareInfoFunction>
    {
        public string Name { get; }
        public int ParameterCount { get; }
        public Func<IEnumerable<IEnumerable<HardwareInfoEntry>>, IEnumerable<HardwareInfoEntry>> Evaluator { get; }
        
        public HardwareInfoFunction(string name, int parameterCount, Func<IEnumerable<IEnumerable<HardwareInfoEntry>>, IEnumerable<HardwareInfoEntry>> evaluator)
        {
            Name = name;
            ParameterCount = parameterCount;
            Evaluator = evaluator;
        }
        private static IEnumerable<HardwareInfoEntry> FnRound(IEnumerable<IEnumerable<HardwareInfoEntry>> input)
        {
            var yielded = false;
            var arg = input.Single();
            foreach (var value in arg)
            {
                yielded = true;
                yield return new HardwareInfoEntry(() => (float)Math.Round(value.Value), value.Unit,value.Provider);
            }
            if (!yielded)
            {
                yield return new HardwareInfoEntry(() => float.NaN, "",null);
            }
        }
        private static IEnumerable<HardwareInfoEntry> FnRound1(IEnumerable<IEnumerable<HardwareInfoEntry>> input)
        {
            var yielded = false;
            var arg = input.Single();
            foreach (var value in arg)
            {
                yielded = true;
                yield return new HardwareInfoEntry(() => (float)Math.Round(value.Value,1), value.Unit,value.Provider);
            }
            if (!yielded)
            {
                yield return new HardwareInfoEntry(() => float.NaN, "",null);
            }
        }
        private static IEnumerable<HardwareInfoEntry> FnPast(IEnumerable<IEnumerable<HardwareInfoEntry>> input)
        {
            var arg = input.Single();
            foreach (var value in arg)
            {
                yield return value;
            }
        }
        public bool Equals(HardwareInfoFunction other)
        {
            return (other.Name.Equals(Name, StringComparison.Ordinal));
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Name);
            
        }
        public static readonly HardwareInfoFunction Count = new("count",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [new HardwareInfoEntry(() => input.Single().Count(), "",null)]);
        public static readonly HardwareInfoFunction First = new("first",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [input.Single().First()]);
        public static readonly HardwareInfoFunction Last = new("last",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [input.Single().Last()]);
        public static readonly HardwareInfoFunction Avg = new("avg",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [new HardwareInfoEntry(() => input.Single().Select(p => p.Value).Average(), input.Single().Select(p => p.Unit).Distinct().Count() == 1 ? input.Single().First().Unit : "",null)]);
        public static readonly HardwareInfoFunction Sum = new("sum",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [new HardwareInfoEntry(() => input.Single().Select(p => p.Value).Sum(), input.Single().Select(p => p.Unit).Distinct().Count() == 1 ? input.Single().First().Unit : "", null)]);
        public static readonly HardwareInfoFunction Min = new("min",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [new HardwareInfoEntry(() => input.Single().Select(p => p.Value).Min(), input.Single().Select(p => p.Unit).Distinct().Count() == 1 ? input.Single().First().Unit : "",null)]);
        public static readonly HardwareInfoFunction Max = new("max",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => [new HardwareInfoEntry(() => input.Single().Select(p => p.Value).Max(), input.Single().Select(p => p.Unit).Distinct().Count() == 1 ? input.Single().First().Unit : "",null)]);
        public static readonly HardwareInfoFunction Round = new("round",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => FnRound(input));
        public static readonly HardwareInfoFunction Round1 = new("round1",1, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => FnRound1(input));
        public static readonly HardwareInfoFunction Past = new("past", 2, (IEnumerable<IEnumerable<HardwareInfoEntry>> input) => FnPast(input));
    }
    
    public abstract partial class HardwareInfoExpression: ICloneable, IEquatable<HardwareInfoExpression> 
    {
        #region Cursors
        private abstract partial class Cursor
        {
            public Cursor(string source = "<stream>", long position = 0, int line = 1, int column = 1, int tabWidth = 4) {
                Codepoint = -2;
                Source = source;
                Position = position;
                Line = line;
                Text = null;
                Column = column;
                TabWidth = tabWidth;
            }
            public int Codepoint { get; private set; }
            public string? Text { get; private set; }
            public int Line { get; private set; }
            public int Column { get; private set; }
            public string Source { get; private set; }
            public long Position { get; private set; }
            public int TabWidth { get; private set; }
            protected abstract int ReadNext();
            public bool SkipWhitespace()
            {
                var result = false;
                while (!string.IsNullOrEmpty(Text) && char.IsWhiteSpace(Text, 0))
                {
                    result = true;
                    Advance();
                }
                return result;
            }
            public int Advance()
            {
                var first = false;
                switch (Codepoint)
                {
                    case -2:
                        first = true;
                        break;
                    case '\n':
                        ++Line;
                        Column = 1;
                        break;
                    case '\r':
                        Column = 1;
                        break;
                    case '\t':
                        Column = ((Column - 1) / TabWidth) * (TabWidth + 1);
                        break;
                    default:
                        if (!first && Codepoint >= 32)
                        {
                            ++Column;
                        }
                        break;

                }
                int ch1 = ReadNext();
                if (ch1 == -1)
                {
                    Codepoint = -1;
                    Text = null;
                    return -1;
                }
                if (char.IsLowSurrogate((char)ch1))
                {
                    int ch2 = ReadNext();
                    if (ch2 == -1)
                    {
                        throw new System.IO.IOException($"Unterminated Unicode surrogate sequence at line {Line}, column {Column}, position {Position}");
                    } else if (!char.IsHighSurrogate((char)ch2))
                    {
                        throw new System.IO.IOException($"Invalid Unicode surrogate sequence at line {Line}, column {Column}, position {Position}");
                    } else
                    {
                        Codepoint = char.ConvertToUtf32((char)ch1, (char)ch2);
                        Text = char.ConvertFromUtf32(Codepoint);
                    }
                } else
                {
                    Codepoint = (char)ch1;
                    Text = char.ConvertFromUtf32(Codepoint);
                }
                if (!first)
                {
                    ++Position;
                }
                return Codepoint;
            }
        }
        private sealed partial class StringCursor : Cursor
        {
            int _index;
            public StringCursor(string source, long position = 0, int line = 1, int column = 1, int tabWidth = 4) : base(source, position, line, column, tabWidth)
            {
                _index = (int)position;
            }
            protected override int ReadNext()
            {
                if (_index < Source.Length)
                {
                    return Source[(int)_index++];
                }
                return -1;
            }
        }
        private sealed partial class TextReaderCursor : Cursor
        {
            public TextReaderCursor(TextReader reader, string source = "<stream>", long position = 0, int line = 1, int column = 1, int tabWidth = 4) : base(source, position, line, column, tabWidth)
            {
                Reader = reader;
            }
            private TextReader Reader { get; }
            protected override int ReadNext()
            {
                return Reader.Read();
            }
        }
        #endregion // Cursors
        public HardwareInfoExpression()
        {
            _ancestors = new Lazy<IList<IHardwareInfoExpressionNonTerminalNode>>(this.GetAncestors<IHardwareInfoExpressionNonTerminalNode>().ToLazyList());
            _predecessors = new Lazy<IList<IHardwareInfoExpressionNode>>(this.GetPredecessors<IHardwareInfoExpressionNode>().ToLazyList());
            _successors = new Lazy<IList<IHardwareInfoExpressionNode>>(this.GetSuccessors<IHardwareInfoExpressionNode>().ToLazyList());
        }
        
        public HardwareInfoLineInfo Location { get; set; }
        public abstract IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo);
        public abstract bool IsTerminal { get; }

        public static HardwareInfoExpression Parse(string text, long position = 0, int line = 1, int column = 1, int tabWidth = 4)
        {
            Cursor cursor = new StringCursor(text, position, line, column, tabWidth);
            cursor.Advance();
            var result = ParseImpl(cursor, ParsePrimary(cursor), 0);
            if (result == null)
            {
                throw new HardwareInfoParseException("Unknown parse error", new HardwareInfoLineInfo(position, line, column, text));
            }
            return result;
        }
        public static HardwareInfoExpression Read(TextReader reader, string source = "<stream>", long position = 0, int line = 1, int column = 1, int tabWidth = 4)
        {
            Cursor cursor = new TextReaderCursor(reader, source, position, line, column, tabWidth);
            cursor.Advance();
            var result = ParseImpl(cursor, ParsePrimary(cursor), 0);
            if (result == null)
            {
                throw new HardwareInfoParseException("Unknown parse error", new HardwareInfoLineInfo(position, line, column, source));
            }
            return result;
        }
        private static HardwareInfoLineInfo Mark(Cursor cursor)
        {
            return new HardwareInfoLineInfo(cursor.Position, cursor.Line, cursor.Column, cursor.Source);
        }
        [DebuggerHidden]
        private static void Expecting(Cursor cursor, HardwareInfoLineInfo mark, params int[] codepoints)
        {
            if (cursor.Codepoint == -2)
            {
                throw new InvalidOperationException("Cursor is before start");
            }
            if (codepoints.Length == 0)
            {
                if (cursor.Codepoint == -1)
                {
                    throw new HardwareInfoParseException("Unexpected end of input", mark);
                }
                return;
            }
            int idx = Array.IndexOf(codepoints, cursor.Codepoint);
            if (idx != -1) return;
            var sb = new StringBuilder();
            var i = 0;
            foreach (int codepoint in codepoints)
            {
                if (i > 0)
                {
                    if (i == codepoints.Length - 1)
                    {
                        sb.Append(", or ");
                    }
                    else
                    {
                        sb.Append(", ");
                    }
                }
                sb.Append(char.ConvertFromUtf32(codepoint));
            }
            throw new HardwareInfoParseException($"Expecting {sb}", mark);
        }
        private static string ParseIdentifier(Cursor cursor)
        {
            var mark = Mark(cursor);
            Expecting(cursor, mark);
            if (cursor.Codepoint != '_' && !char.IsLetter(cursor.Text!, 0))
            {
                throw new HardwareInfoParseException("Expecting an identifier", mark);
            }
            var result = new StringBuilder();
            result.Append(cursor.Text);
            while (cursor.Advance() > -1 && ('_' == cursor.Codepoint || char.IsLetterOrDigit(cursor.Text!, 0))) result.Append(cursor.Text!);
            return result.ToString();
        }
        private static HardwareInfoExpression ParseInvokeArg(HardwareInfoFunction func, Cursor cursor)
        {
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            mark = Mark(cursor);
            Expecting(cursor, mark);
            var expr = ParseImpl(cursor, ParsePrimary(cursor), 0);
            if (expr == null)
                throw new HardwareInfoParseException("Unknown parse exception", mark);
            cursor.SkipWhitespace();
            return expr;
        }
        private static HardwareInfoExpression ParsePath(Cursor cursor)
        {
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            Expecting(cursor, mark, '/');
            var path = new StringBuilder();
            while (cursor.Codepoint != -1 && cursor.Codepoint != ')' && !char.IsWhiteSpace(cursor.Text!, 0))
            {
                path.Append(cursor.Text);
                cursor.Advance();
            }
            return new HardwareInfoPathExpression(path.ToString());
        }
        private static HardwareInfoExpression ParseMatch(Cursor cursor)
        {
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            Expecting(cursor, mark, '\'');
            cursor.Advance();
            Expecting(cursor, mark);
            mark = Mark(cursor);
            var regexStr = new StringBuilder();
            bool inEscape = false;
            int braceDepth = 0;
            while (cursor.Codepoint != -1)
            {
                if (inEscape == false && braceDepth == 0)
                {
                    if (cursor.Codepoint == '\'')
                    {
                        break;
                    }
                    else if (cursor.Codepoint == '\\')
                    {
                        inEscape = true;
                        cursor.Advance();
                        Expecting(cursor, mark);
                    }
                    else if (cursor.Codepoint == '[')
                    {
                        ++braceDepth;
                    }
                }
                else if (inEscape == false && braceDepth > 0) // Handle characters inside [...] when not escaped
                {
                    if (cursor.Codepoint == '\\')
                    {
                        inEscape = true;
                        cursor.Advance();
                        Expecting(cursor, mark);
                    }
                    else if (cursor.Codepoint == ']')
                    {
                        --braceDepth;
                    }
                }

                if (inEscape)
                {
                    if (cursor.Codepoint == '\'')
                    {
                        regexStr.Append('\'');
                    }
                    else
                    {
                        regexStr.Append('\\');
                        regexStr.Append(cursor.Text);
                    }
                }
                else
                {
                    regexStr.Append(cursor.Text);
                }

                cursor.Advance();
                inEscape = false;
            }
            Expecting(cursor, mark, '\'');
            cursor.Advance();

            try
            {
                var regex = new Regex(regexStr.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.ExplicitCapture);
                return new HardwareInfoMatchExpression(regex);
            }
            catch (RegexParseException rpe)
            {
                var cleanup = CleanRegexErrorMessage(rpe.Message);
                mark.Position += cleanup.Item2;
                mark.Column += cleanup.Item2;
                throw new HardwareInfoParseException(cleanup.Item1, mark);
            }
        }
        static (string, int) CleanRegexErrorMessage(string msg)
        {
            ArgumentNullException.ThrowIfNull(msg, nameof(msg));

            var match = Regex.Match(msg, @"(.*?)at offset (\d+)\.?(.*)");
            if (!match.Success) return (msg, 0);

            var cleanMsg = match.Groups[1].Value.TrimEnd() + match.Groups[3].Value;
            var offset = int.Parse(match.Groups[2].Value) - 1;

            return (cleanMsg, offset);
        }

        private static int GetPrecedence(int op)
        {
            switch (op)
            {
                case '+':
                case '-':
                    return 0;
                case '*':
                case '/':
                    return 1;
                case '|':
                    return 2;
                default:
                    return -1;
            }
        }
        private static bool IsRightAssociative(int op)
        {
            switch (op)
            {
                case '|':
                    return true;
                default:
                    return false;
            }
        }
        private static bool IsOperator(int op)
        {
            switch (op)
            {
                case '+':
                case '-':
                case '*':
                case '/':
                case '|':
                    return true;
                default:
                    return false;
            }
        }
        private static HardwareInfoExpression CreateOperatorExpression(HardwareInfoLineInfo mark, HardwareInfoExpression left, int op, HardwareInfoExpression right)
        {
            HardwareInfoExpression? expr;
            switch (op)
            {
                case '+':
                    expr = new HardwareInfoAddExpression(left, right);
                    break;
                case '-':
                    expr = new HardwareInfoSubtractExpression(left, right);
                    break;
                case '*':
                    expr = new HardwareInfoMultiplyExpression(left, right);
                    break;
                case '/':
                    expr = new HardwareInfoDivideExpression(left, right);
                    break;
                case '|':
                    expr = new HardwareInfoUnionExpression(left, right);
                    break;
                default:
#if DEBUG
                    System.Diagnostics.Debug.Assert(false, "Error in expression parsing code");
#endif
                    expr = null;
                    break;
            }
            expr!.Location = mark;
            return expr;
        }
        private static HardwareInfoExpression? ParseImpl(Cursor cursor, HardwareInfoExpression? left, int minPrecedence)
        {
            // while the next token is a binary operator whose precedence is >= min_precedence
            while (true)
            {
                cursor.SkipWhitespace();
                var mark = Mark(cursor);
                var op = cursor.Codepoint;
                if (op == -1 || !IsOperator(op) || GetPrecedence(op) < minPrecedence)
                {
                    break;
                }
                cursor.Advance();

                var right = ParsePrimary(cursor);

                // while the next token is
                //   (1) a binary operator whose precedence is greater than op's, or
                //   (2) a right-associative operator whose precedence is equal to op's
                while (true)
                {
                    cursor.SkipWhitespace();
                    mark = Mark(cursor);
                    var lookahead = cursor.Codepoint;
                    if (lookahead == -1 ||
                    !IsOperator(lookahead) ||
                    !(GetPrecedence(lookahead) > GetPrecedence(op) ||
                      IsRightAssociative(lookahead) == true && GetPrecedence(lookahead) == GetPrecedence(op)))
                    {
                        break;
                    }
                    else
                    {
                        right = ParseImpl(cursor, right, GetPrecedence(lookahead));
                        if (right != null)
                        {
                            right = ParseModifier(cursor, right);
                        }
                    }
                }
                if (left == null || right == null)
                {
                    throw new HardwareInfoParseException("Unknown parse exception", mark);
                }
                HardwareInfoExpression expr = CreateOperatorExpression(mark, left, op, right);
                left = expr;
                if (left != null)
                {
                    left = ParseModifier(cursor, left);
                }
            }
            if (left != null)
            {
                left = ParseModifier(cursor, left);
            }
            return left;
        }
        private static HardwareInfoExpression ParseLiteral(Cursor cursor)
        {
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            long num = 0; long frac = 0; long exp = 0;
            var neg = false;
            var expNeg = false;
            int state = 0;
            while (state != -1 && cursor.Codepoint != -1)
            {
                switch (state)
                {
                    case 0:
                        if (cursor.Codepoint == '-')
                        {
                            neg = true;
                            state = 1;
                            cursor.Advance();
                        }
                        else if (cursor.Codepoint == '+')
                        {
                            state = 1;
                            cursor.Advance();
                        }
                        else
                        {
                            Expecting(cursor, mark, '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                            num = cursor.Codepoint - '0';
                            state = 1;
                            cursor.Advance();
                        }
                        break;
                    case 1:
                        if (cursor.Codepoint < 128 && "0123456789".Contains((char)cursor.Codepoint)) {
                            num *= 10;
                            num += cursor.Codepoint - '0';
                            cursor.Advance();
                        } else
                        {
                            state = 2;
                        }
                        break;
                    case 2:
                        if (cursor.Codepoint == -1) continue; // exit
                        if (cursor.Codepoint == '.')
                        {
                            state = 3;
                            cursor.Advance();
                        } else if (cursor.Codepoint == 'E' || cursor.Codepoint == 'e')
                        {
                            state = 4;
                            cursor.Advance();
                        } else
                        {
                            if (cursor.Codepoint < 0 || cursor.Codepoint >= 128 || !"Ee.".Contains((char)cursor.Codepoint))
                            {
                                state = -1;
                                continue; // exit
                            }
                        }
                        break;
                    case 3:
                        if (cursor.Codepoint < 128 && "0123456789".Contains((char)cursor.Codepoint))
                        {
                            frac *= 10;
                            frac += cursor.Codepoint - '0';
                            cursor.Advance();
                        }
                        else
                        {
                            if (cursor.Codepoint == 'E' || cursor.Codepoint == 'e')
                            {
                                state = 4;
                                cursor.Advance();
                            } else
                            {
                                state = -1;
                                continue; // exit
                            }
                        }
                        break;
                    case 4:
                        if (cursor.Codepoint == '-')
                        {
                            expNeg = true;
                            state = 5;
                            cursor.Advance();
                        }
                        else if (cursor.Codepoint == '+')
                        {
                            state = 5;
                            cursor.Advance();
                        }
                        else
                        {
                            Expecting(cursor, mark, '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                            exp = cursor.Codepoint - '0';
                            cursor.Advance();
                            state = 5;
                        }
                        break;
                    case 5:
                        if (cursor.Codepoint < 128 && "0123456789".Contains((char)cursor.Codepoint))
                        {
                            exp *= 10;
                            exp += cursor.Codepoint - '0';
                            cursor.Advance();
                        }
                        else
                        {
                            state = -1;
                            continue; // exit
                        }
                        break;
                }
            }
            if (neg) { num = -num; }
            float result = neg ? -num : num;
            float tmp = frac;
            while (tmp >= 1) { tmp /= 10; }
            if (neg) tmp = -tmp;
            result += tmp;
            if (!expNeg)
            {
                result = (float)(result * Math.Pow(10, exp));
            } else
            {
                result = (float)(result / Math.Pow(10, exp));
            }
            return new HardwareInfoLiteralExpression(result);
        }
        private static HardwareInfoExpression ParseModifier(Cursor cursor, HardwareInfoExpression expr)
        {
            ArgumentNullException.ThrowIfNull(expr, nameof(expr));
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            if (cursor.Codepoint == -1) return expr;
            string unit = "";
            while (cursor.Codepoint >= 0 && !IsOperator(cursor.Codepoint) && !char.IsWhiteSpace((char)cursor.Codepoint) && cursor.Codepoint != '|' && cursor.Codepoint!=',' && cursor.Codepoint != '(' && cursor.Codepoint != ')' && cursor.Codepoint != '\'' && cursor.Codepoint != '-' && !char.IsDigit((char)cursor.Codepoint))
            {
                unit += cursor.Text;
                cursor.Advance();
            }
            if (unit.Length == 0)
            {
                return expr;
            }
            return new HardwareInfoUnitExpression(expr, unit);
        }
        private static HardwareInfoExpression ParsePrimary(Cursor cursor)
        {
            HardwareInfoExpression? expr = null;
            cursor.SkipWhitespace();
            var mark = Mark(cursor);
            Expecting(cursor, mark);
            if (cursor.Codepoint == '(')
            {
                cursor.Advance();
                mark = Mark(cursor);
                Expecting(cursor, mark);
                expr = ParseImpl(cursor, ParsePrimary(cursor), 0);
                Expecting(cursor, mark, ')');
                cursor.Advance();
                if (expr != null)
                {
                    expr = ParseModifier(cursor, expr);
                }
            }
            else if (cursor.Codepoint == '_' || char.IsLetter(cursor.Text!, 0))
            {
                mark = Mark(cursor);
                var ident = ParseIdentifier(cursor);
                HardwareInfoFunction? func;
                switch (ident)
                {
                    case "count":
                        func = HardwareInfoFunction.Count;
                        break;
                    case "first":
                        func = HardwareInfoFunction.First;
                        break;
                    case "last":
                        func = HardwareInfoFunction.Last;
                        break;
                    case "avg":
                        func = HardwareInfoFunction.Avg;
                        break;
                    case "sum":
                        func = HardwareInfoFunction.Sum;
                        break;
                    case "min":
                        func = HardwareInfoFunction.Min;
                        break;
                    case "max":
                        func = HardwareInfoFunction.Max;
                        break;
                    case "past":
                        func = HardwareInfoFunction.Past;
                        break;
                    case "round":
                        func = HardwareInfoFunction.Round;
                        break;
                    case "round1":
                        func = HardwareInfoFunction.Round1;
                        break;
                    default:
                        func = null;
                        break;
                }
                cursor.SkipWhitespace();
                if (cursor.Codepoint == '(')
                {
                    if (func != null)
                    {
                        var funcExpr = new HardwareInfoInvokeExpression(func.Value);
                        mark = Mark(cursor);
                        Expecting(cursor, mark, '(');
                        cursor.Advance();
                        cursor.SkipWhitespace();
                        var wasComma = false;
                        while (cursor.Codepoint != ')')
                        {
                            mark = Mark(cursor);
                            Expecting(cursor, mark);
                            expr = ParseInvokeArg(func.Value, cursor);
                            wasComma = false;
                            funcExpr.Children.Add(expr);
                            cursor.SkipWhitespace();
                            mark = Mark(cursor);
                            Expecting(cursor, mark,',',')');
                            if(cursor.Codepoint==',')
                            {
                                cursor.Advance();
                                cursor.SkipWhitespace();
                                wasComma = true;
                            }
                        }
                        if(wasComma)
                        {
                            throw new HardwareInfoParseException("Unexpected , in function argunment list", mark);
                        }
                        mark = Mark(cursor);
                        Expecting(cursor, mark, ')');
                        cursor.Advance();
                        if(funcExpr.Children.Count<func.Value.ParameterCount)
                        {
                            throw new HardwareInfoParseException("Too few arguments to function", mark);
                        } else if(funcExpr.Children.Count > func.Value.ParameterCount) {
                            throw new HardwareInfoParseException("Too many arguments to function", mark);
                        }
                        expr = funcExpr;
                    }
                    else
                    {
                        throw new HardwareInfoParseException($"Unknown function {ident}", mark);
                    }
                } else if (expr != null)
                {
                    expr = new HardwareInfoUnitExpression(expr, ident);
                } else
                {
                    throw new HardwareInfoParseException($"Unknown function {ident}", mark);
                }
            }
            else if (cursor.Codepoint == '/')
            {
                expr = ParsePath(cursor);
            } else if (cursor.Codepoint == '\'')
            {
                expr = ParseMatch(cursor);

            } else if (cursor.Codepoint == '-' || cursor.Codepoint == '+' || (cursor.Codepoint >= '0' && cursor.Codepoint <= '9'))
            {
                expr = ParseLiteral(cursor);
            }
            if (expr == null)
            {
                // will throw
                Expecting(cursor, mark, '(', '/', '\'', 'a', 'c', 'f', 'l', 'm', 'p', 'r', 's', '+', '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
#if DEBUG
                System.Diagnostics.Debug.Assert(false, "The expecting did not catch all invalid cases above");
#endif
            }
            
            return ParseModifier(cursor, expr!);
        }
        public abstract HardwareInfoExpression Clone();
        object ICloneable.Clone()
        {
            return Clone();
        }

        public abstract bool Equals(HardwareInfoExpression? other);

        public override bool Equals(object? obj)
        {
            if(obj is  HardwareInfoExpression expr) { return Equals(expr); }
            return false;
        }
        public abstract override int GetHashCode();
    }
    public abstract partial class HardwareInfoTerminalExpression : HardwareInfoExpression
    {
        public override bool IsTerminal => true;
    }
    public sealed partial class HardwareInfoEmptyExpression : HardwareInfoTerminalExpression
    {
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            return Array.Empty<HardwareInfoEntry>();
        }
        public override string ToString()
        {
            return "";
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoEmptyExpression();
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoEmptyExpression)
            {
                return true;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return GetType().GetHashCode();
        }
    }

    public abstract partial class HardwareInfoNonTerminalExpression : HardwareInfoExpression
    {
        public HardwareInfoNonTerminalExpression()
        {
            _children = new Lazy<IList<IHardwareInfoExpressionNode>>(this.GetChildren<IHardwareInfoExpressionNode>().ToLazyList());
            _descendants = new Lazy<IList<IHardwareInfoExpressionNode>>(this.GetDescendants<IHardwareInfoExpressionNode>().ToLazyList());
        }
        public override bool IsTerminal => false;
    }
    public abstract partial class HardwareInfoUnaryExpression : HardwareInfoNonTerminalExpression
    {
        public HardwareInfoUnaryExpression(HardwareInfoExpression expression) { 
            Expression = expression;
        }
        public HardwareInfoUnaryExpression() : this(new HardwareInfoEmptyExpression()) { }
        public HardwareInfoExpression Expression { get; set; } = new HardwareInfoEmptyExpression();
    }
    public abstract partial class HardwareInfoBinaryExpression : HardwareInfoNonTerminalExpression
    {
        public HardwareInfoBinaryExpression(HardwareInfoExpression left, HardwareInfoExpression right) { ArgumentNullException.ThrowIfNull(left, nameof(left)); Left = left; ArgumentNullException.ThrowIfNull(right, nameof(right)); Right = right; }
        public HardwareInfoBinaryExpression() : this(new HardwareInfoEmptyExpression(), new HardwareInfoEmptyExpression()) { }
        public HardwareInfoExpression Left { get; set; }
        public HardwareInfoExpression Right { get; set; }
    }
    public abstract partial class HardwareInfoNAryExpression : HardwareInfoNonTerminalExpression
    {
        protected HardwareInfoNAryExpression(params HardwareInfoExpression[] exprs) { if (exprs != null) { Children.AddRange(exprs); } }
        public List<HardwareInfoExpression> Children { get; } = new List<HardwareInfoExpression>();
    }
    public partial class HardwareInfoLiteralExpression : HardwareInfoTerminalExpression
    {
        public HardwareInfoLiteralExpression() { }
        public HardwareInfoLiteralExpression(float value) { Value = value; }
        public float Value { get; set; }
        public override string ToString()
        {
            if (float.IsNaN(Value))
            {
                return "NaN";
            }
            return Value.ToString("G");
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            yield return new HardwareInfoEntry(() => Value, "",null);
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoLiteralExpression(Value);
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoLiteralExpression rhs)
            {
                if (float.IsNaN(rhs.Value) && float.IsNaN(Value)) return true;
                return rhs.Value == rhs.Value;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Value);
        }
    }
    public partial class HardwareInfoUnitExpression : HardwareInfoUnaryExpression
    {
        public HardwareInfoUnitExpression() { }
        public HardwareInfoUnitExpression(HardwareInfoExpression expression, string unit) : base(expression) {
            Unit = unit;
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoUnitExpression(Expression.Clone(), Unit);
        }
        public string Unit { get; set; } = string.Empty;
        public override string ToString()
        {
            var expr = Expression == null ? "<ERROR>" : Expression.ToString();
            if (Expression == null || Expression.IsTerminal)
            {
                return $"{expr}{Unit}";
            } else
            {
                return $"({expr}){Unit}";
            }
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            foreach (var result in Expression!.Evaluate(hardwareInfo))
            {
                yield return new HardwareInfoEntry(result.Path, result.Getter, Unit, result.Provider);
            }
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if(other is HardwareInfoUnitExpression unit)
            {
                return unit.Unit.Equals(Unit, StringComparison.Ordinal) && Expression.Equals(unit.Expression);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Unit, Expression);
        }
    }
    public abstract partial class HardwareInfoQueryExpression : HardwareInfoTerminalExpression
    {
        public HardwareInfoQueryExpression() { }

    }
    public partial class HardwareInfoPathExpression : HardwareInfoQueryExpression {
        public HardwareInfoPathExpression() :this("") { }
        public HardwareInfoPathExpression(string path) {
            Path = path;
        }
        public string Path { get; set; }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            return hardwareInfo.Query(this);
        }
        public override string ToString()
        {
            return Path!;
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoPathExpression(Path);
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if(other is HardwareInfoPathExpression rhs)
            {
                return Path.Equals(rhs.Path, StringComparison.Ordinal);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Path);
        }
    }
    public partial class HardwareInfoMatchExpression : HardwareInfoQueryExpression
    {
        private static Regex _all = new Regex(".+",RegexOptions.CultureInvariant | RegexOptions.Singleline);
        public HardwareInfoMatchExpression() : this(_all) { }
        public HardwareInfoMatchExpression(Regex match)
        {
            Match = match;
        }
        public Regex Match { get; set; }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            ArgumentNullException.ThrowIfNull(hardwareInfo, nameof(hardwareInfo));
            if (Match == null) throw new InvalidOperationException("The Match was not set");

            return hardwareInfo!.Query(this);
        }
        public override string ToString()
        {
            return "\'" + Match!.ToString() + "\'";
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoMatchExpression(Match);
        }
        public static readonly HardwareInfoMatchExpression MatchAll = new HardwareInfoMatchExpression();

        public override bool Equals(HardwareInfoExpression? other)
        {
            if(other is HardwareInfoMatchExpression rhs)
            {
                return Match.ToString().Equals(rhs.Match.ToString(),StringComparison.Ordinal);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Match.ToString());
        }
    }
    public partial class HardwareInfoAddExpression : HardwareInfoBinaryExpression
    {
        public HardwareInfoAddExpression() { }
        public HardwareInfoAddExpression(HardwareInfoExpression left, HardwareInfoExpression right) : base(left, right) { }
        private string? GetSubWithPrecedence(HardwareInfoExpression expression)
        {
            var str = expression!.ToString();
            return str;
        }
        public override string ToString()
        {
            return GetSubWithPrecedence(Left!) + " + " + GetSubWithPrecedence(Right!);
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            var right = Right!.Evaluate(hardwareInfo).Single();
            // we want it to throw if either is null
            foreach (var value in Left!.Evaluate(hardwareInfo))
            {
                if (value.Unit.Equals(right.Unit, StringComparison.Ordinal))
                {
                    yield return new HardwareInfoEntry(null, () => value.Value + right.Value, value.Unit, object.ReferenceEquals(value.Provider,right.Provider)?value.Provider:null);
                } else
                {
                    yield return new HardwareInfoEntry(null, () => value.Value + right.Value, "", object.ReferenceEquals(value.Provider, right.Provider) ? value.Provider : null);
                }
            }
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoAddExpression(Left.Clone(), Right.Clone());
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoAddExpression rhs)
            {
                return rhs.Left.Equals(Left) && rhs.Right.Equals(Right); 
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType() ,Left, Right);
        }
    }
    public partial class HardwareInfoSubtractExpression : HardwareInfoBinaryExpression
    {
        public HardwareInfoSubtractExpression() { }
        public HardwareInfoSubtractExpression(HardwareInfoExpression left, HardwareInfoExpression right) : base(left, right) { }
        private string? GetSubWithPrecedence(HardwareInfoExpression expression)
        {
            var str = expression!.ToString();
            return str;
        }
        public override string ToString()
        {
            return GetSubWithPrecedence(Left!) + " - " + GetSubWithPrecedence(Right!);
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            var right = Right!.Evaluate(hardwareInfo).Single();
            // we want it to throw if either is null
            foreach (var value in Left!.Evaluate(hardwareInfo))
            {
                if (value.Unit.Equals(right.Unit, StringComparison.Ordinal))
                {
                    yield return new HardwareInfoEntry(null, () => value.Value - right.Value, value.Unit, object.ReferenceEquals(value.Provider, right.Provider) ? value.Provider : null);
                }
                else
                {
                    yield return new HardwareInfoEntry(null, () => value.Value + right.Value, "", object.ReferenceEquals(value.Provider, right.Provider) ? value.Provider : null);
                }
            }
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoSubtractExpression(Left.Clone(), Right.Clone());
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoSubtractExpression rhs)
            {
                return rhs.Left.Equals(Left) && rhs.Right.Equals(Right);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Left, Right);
        }
    }
    public partial class HardwareInfoMultiplyExpression : HardwareInfoBinaryExpression
    {
        public HardwareInfoMultiplyExpression() { }
        public HardwareInfoMultiplyExpression(HardwareInfoExpression left, HardwareInfoExpression right) : base(left, right) { }
        private string? GetSubWithPrecedence(HardwareInfoExpression expression)
        {
            var str = expression!.ToString();
            if (expression is HardwareInfoAddExpression || expression is HardwareInfoSubtractExpression)
            {
                return "(" + str + ")";
            }
            return str;
        }
        public override string ToString()
        {
            return GetSubWithPrecedence(Left!) + " * " + GetSubWithPrecedence(Right!);
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            var right = Right!.Evaluate(hardwareInfo).Single();
            // we want it to throw if either is null
            foreach (var value in Left!.Evaluate(hardwareInfo))
            {
                yield return new HardwareInfoEntry(() => value.Value * right.Value, "", object.ReferenceEquals(value.Provider, right.Provider) ? value.Provider : null);
            }
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoMultiplyExpression(Left.Clone(), Right.Clone());
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoMultiplyExpression rhs)
            {
                return rhs.Left.Equals(Left) && rhs.Right.Equals(Right);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Left, Right);
        }
    }
    public partial class HardwareInfoDivideExpression : HardwareInfoBinaryExpression
    {
        public HardwareInfoDivideExpression() { }
        public HardwareInfoDivideExpression(HardwareInfoExpression left, HardwareInfoExpression right) : base(left, right) { }
        private string? GetSubWithPrecedence(HardwareInfoExpression expression)
        {
            var str = expression!.ToString();
            if (expression is HardwareInfoAddExpression || expression is HardwareInfoSubtractExpression)
            {
                return "(" + str + ")";
            }
            return str;
        }
        public override string ToString()
        {
            return GetSubWithPrecedence(Left!) + " / " + GetSubWithPrecedence(Right!);
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            var right = Right!.Evaluate(hardwareInfo).Single();
            // we want it to throw if either is null
            foreach (var value in Left!.Evaluate(hardwareInfo))
            {
                yield return new HardwareInfoEntry(null, () => value.Value / right.Value, "", object.ReferenceEquals(value.Provider, right.Provider) ? value.Provider : null);
            }
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoDivideExpression(Left!.Clone(), Right!.Clone());
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoDivideExpression rhs)
            {
                return rhs.Left.Equals(Left) && rhs.Right.Equals(Right);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(GetType(), Left, Right);
        }
    }
    public partial class HardwareInfoInvokeExpression : HardwareInfoNAryExpression
    {
        public HardwareInfoInvokeExpression() { }
        public HardwareInfoInvokeExpression(HardwareInfoFunction function, params HardwareInfoExpression[] expressions) : base(expressions) { Function = function; }

        public HardwareInfoFunction Function { get; set; }
        private static long TimeUnitToMs(float value, string unit)
        {
            if (float.IsNaN(value)) return 0;
            if (unit.StartsWith("day", StringComparison.OrdinalIgnoreCase) || unit.Equals("d", StringComparison.OrdinalIgnoreCase))
            {
                return (long)(value * 1000 * 60 * 60 * 24);
            }
            if (unit.StartsWith("hr", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("hour", StringComparison.OrdinalIgnoreCase) || unit.Equals("h", StringComparison.OrdinalIgnoreCase))
            {
                return (long)(value * 1000 * 60 * 60);
            }
            if (string.IsNullOrEmpty(unit) || unit.StartsWith("min", StringComparison.OrdinalIgnoreCase) || unit.Equals("m", StringComparison.OrdinalIgnoreCase))
            {
                return (long)(value * 1000 * 60);
            }
            if (unit.StartsWith("sec", StringComparison.OrdinalIgnoreCase) || unit.Equals("s", StringComparison.OrdinalIgnoreCase))
            {
                return (long)(value * 1000);
            }
            if (unit.StartsWith("milli", StringComparison.OrdinalIgnoreCase) || unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
            {
                return (long)value;
            }
            throw new ArgumentException($"The unit \"{unit}\" was not recognized", nameof(unit));
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            // we handle tracking separately here
            if (Function.Name == "past")
            {
                var period = Children[0].Evaluate(hardwareInfo).Single();
                var ms = TimeUnitToMs(period.Value,period.Unit);
                foreach(var value in hardwareInfo.Track(Children[1],ms))
                {
                    yield return new HardwareInfoEntry(()=>value.Value,value.Unit,null);
                }
            }
            else
            {
                // throw if null
                foreach (var result in Function.Evaluator(Children.Select(p => p.Evaluate(hardwareInfo))))
                {
                    yield return result;
                }
            }
        }
        public override string ToString()
        {
            // throw if null
            return Function.Name + "(" + string.Join(", ",Children.Select(p=>p.ToString())) + ")";
        }
        public override HardwareInfoExpression Clone()
        {
            var children = new HardwareInfoExpression[Children.Count];
            for (int i = 0;i<children.Length;++i)
            {
                children[i]=Children[i].Clone();
            }
            return new HardwareInfoInvokeExpression(Function, children);
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoInvokeExpression rhs)
            {
                if (!rhs.Function.Equals(Function)) { return false; }
                if (rhs.Children.Count != Children.Count) { return false; }
                for(var i = 0;i<Children.Count;++i)
                {
                    if (!Children[i].Equals(rhs.Children[i])) { return false; }
                }
                return true;
            }
            return false;
        }
        public override int GetHashCode()
        {
            var result = HashCode.Combine(GetType(), Function);
            for(var i = 0;i< Children.Count;++i)
            {
                result = HashCode.Combine(result,Children[i]);
            }
            return result;
        }
    }
    public partial class HardwareInfoUnionExpression : HardwareInfoBinaryExpression
    {
        public HardwareInfoUnionExpression() { }
        public HardwareInfoUnionExpression(HardwareInfoExpression left, HardwareInfoExpression right) : base(left, right) { }
        private string? GetSubWithPrecedence(HardwareInfoExpression expression)
        {
            var str = expression!.ToString();
            if (expression is HardwareInfoUnionExpression)
            {
                return str;
            }
            if (expression is HardwareInfoBinaryExpression)
            {
                return "(" + str + ")";
            }
            return str;
        }
        public override HardwareInfoExpression Clone()
        {
            return new HardwareInfoUnionExpression(Left.Clone(), Right.Clone());
        }
        public override string ToString()
        {
            return GetSubWithPrecedence(Left) + " | " + GetSubWithPrecedence(Right);
        }
        public override IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoCollection hardwareInfo)
        {
            // we want it to throw if either is null
            return Left.Evaluate(hardwareInfo).Union(Right.Evaluate(hardwareInfo));
        }
        public override bool Equals(HardwareInfoExpression? other)
        {
            if (other is HardwareInfoUnionExpression rhs)
            {
                return Left.Equals(rhs.Left) && Right.Equals(rhs.Right) || Right.Equals(rhs.Left) && Left.Equals(rhs.Right);
            }
            return false;
        }
        public override int GetHashCode()
        {
            // can't use combine for this because it salts with ordinal
            var hc = Left.GetHashCode() ^ Right.GetHashCode();
            return HashCode.Combine(GetType(), hc);
        }
    }
}
