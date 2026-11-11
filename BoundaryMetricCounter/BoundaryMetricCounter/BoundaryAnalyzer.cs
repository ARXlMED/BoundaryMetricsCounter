using System;
using System.Collections.Generic;
using System.Linq;

namespace BoundaryMetricCounter
{
    /// <summary>
    /// Calculates Gilb boundary-complexity metrics for Rust-like source code.
    /// The parser models control-flow decisions as nested if/else subgraphs.
    /// </summary>
    public static class BoundaryAnalyzer
    {
        public sealed class Result
        {
            public int Sa { get; set; }
            public double So { get; set; }
            public int N { get; set; }
        }

        // -----------------------------------------------------------------
        // Control-flow tree
        // -----------------------------------------------------------------

        private abstract class AstNode
        {
            public abstract int CountNodes();
            public abstract int SumComplexities();
        }

        private sealed class SimpleNode : AstNode
        {
            public override int CountNodes() => 1;
            public override int SumComplexities() => 1;
        }

        private sealed class BlockNode : AstNode
        {
            public List<AstNode> Children { get; } = new();

            public override int CountNodes() =>
                Children.Sum(child => child.CountNodes());

            public override int SumComplexities() =>
                Children.Sum(child => child.SumComplexities());
        }

        private sealed class FunctionNode : AstNode
        {
            public AstNode Body { get; set; } = new BlockNode();

            // Function entry and exit are two vertices.
            // Entry has complexity 1; exit has complexity 0.
            public override int CountNodes() => 2 + Body.CountNodes();
            public override int SumComplexities() => 1 + Body.SumComplexities();
        }

        private sealed class IfNode : AstNode
        {
            public AstNode TrueBranch { get; set; } = new BlockNode();
            public AstNode? FalseBranch { get; set; }

            public override int CountNodes() =>
                1 + TrueBranch.CountNodes() + (FalseBranch?.CountNodes() ?? 0);

            // Corrected complexity of a decision is the size of its
            // subgraph (including the decision vertex) plus the corrected
            // complexities of the vertices contained in its branches.
            public override int SumComplexities()
            {
                int trueCount = TrueBranch.CountNodes();
                int falseCount = FalseBranch?.CountNodes() ?? 0;

                return 1 + trueCount + falseCount
                    + TrueBranch.SumComplexities()
                    + (FalseBranch?.SumComplexities() ?? 0);
            }
        }

        private sealed class WhileNode : AstNode
        {
            public AstNode Body { get; set; } = new BlockNode();

            public override int CountNodes() => 1 + Body.CountNodes();

            public override int SumComplexities() =>
                1 + Body.CountNodes() + Body.SumComplexities();
        }

        // -----------------------------------------------------------------
        // Tokenizer
        // -----------------------------------------------------------------

        private enum TokenKind
        {
            Identifier,
            Number,
            String,
            Symbol
        }

        private sealed class Token
        {
            public TokenKind Kind { get; }
            public string Value { get; }

            public Token(TokenKind kind, string value)
            {
                Kind = kind;
                Value = value;
            }
        }

        private static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < source.Length)
            {
                char c = source[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // Rust line comments.
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i += 2;
                    while (i < source.Length && source[i] != '\n')
                        i++;
                    continue;
                }

                // Rust block comments can be nested.
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    int depth = 1;

                    while (i < source.Length && depth > 0)
                    {
                        if (i + 1 < source.Length &&
                            source[i] == '/' && source[i + 1] == '*')
                        {
                            depth++;
                            i += 2;
                        }
                        else if (i + 1 < source.Length &&
                                 source[i] == '*' && source[i + 1] == '/')
                        {
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    continue;
                }

                // Raw Rust strings: r"...", r#"..."#, br#"..."#, etc.
                if (TryReadRawString(source, ref i, out string? rawString))
                {
                    tokens.Add(new Token(TokenKind.String, rawString));
                    continue;
                }

                // Ordinary and byte/C strings.
                int quoteStart = i;
                if ((c == 'b' || c == 'c') &&
                    i + 1 < source.Length && source[i + 1] == '"')
                {
                    i++;
                    ReadQuoted(source, ref i, '"');
                    tokens.Add(new Token(TokenKind.String,
                        source.Substring(quoteStart, i - quoteStart)));
                    continue;
                }

                if (c == '"')
                {
                    ReadQuoted(source, ref i, '"');
                    tokens.Add(new Token(TokenKind.String,
                        source.Substring(quoteStart, i - quoteStart)));
                    continue;
                }

                // Character literals. A Rust lifetime such as 'static is
                // deliberately left as punctuation + identifier.
                if (c == '\'' && LooksLikeCharacterLiteral(source, i))
                {
                    ReadQuoted(source, ref i, '\'');
                    tokens.Add(new Token(TokenKind.String,
                        source.Substring(quoteStart, i - quoteStart)));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i++;
                    while (i < source.Length &&
                           (char.IsLetterOrDigit(source[i]) ||
                            source[i] == '_' || source[i] == '.'))
                    {
                        // Do not absorb the first dot of a Rust range.
                        if (source[i] == '.' && i + 1 < source.Length &&
                            source[i + 1] == '.')
                            break;
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Number,
                        source.Substring(start, i - start)));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < source.Length &&
                           (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                        i++;

                    tokens.Add(new Token(TokenKind.Identifier,
                        source.Substring(start, i - start)));
                    continue;
                }

                string? op = ReadOperator(source, i);
                if (op != null)
                {
                    tokens.Add(new Token(TokenKind.Symbol, op));
                    i += op.Length;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
                i++;
            }

            return tokens;
        }

        private static bool TryReadRawString(
            string source,
            ref int index,
            out string rawString)
        {
            rawString = string.Empty;
            int start = index;
            int rIndex = index;

            if (rIndex + 1 < source.Length &&
                (source[rIndex] == 'b' || source[rIndex] == 'c') &&
                source[rIndex + 1] == 'r')
            {
                rIndex++;
            }

            if (rIndex >= source.Length || source[rIndex] != 'r')
                return false;

            int quoteIndex = rIndex + 1;
            while (quoteIndex < source.Length && source[quoteIndex] == '#')
                quoteIndex++;

            if (quoteIndex >= source.Length || source[quoteIndex] != '"')
                return false;

            int hashCount = quoteIndex - (rIndex + 1);
            string terminator = "\"" + new string('#', hashCount);
            int end = source.IndexOf(terminator, quoteIndex + 1,
                StringComparison.Ordinal);

            index = end < 0 ? source.Length : end + terminator.Length;
            rawString = source.Substring(start, index - start);
            return true;
        }

        private static void ReadQuoted(string source, ref int index, char quote)
        {
            index++; // opening quote

            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    index += Math.Min(2, source.Length - index);
                    continue;
                }

                if (source[index] == quote)
                {
                    index++;
                    return;
                }

                index++;
            }
        }

        private static bool LooksLikeCharacterLiteral(string source, int index)
        {
            if (index + 2 >= source.Length)
                return false;

            if (source[index + 1] == '\\')
            {
                int closing = source.IndexOf('\'', index + 2);
                return closing >= 0 && closing - index <= 12;
            }

            return source[index + 2] == '\'';
        }

        private static string? ReadOperator(string source, int index)
        {
            // Longest operators must be checked before their prefixes.
            string[] operators =
            {
                "<<=", ">>=", "...", "..=", "->", "=>", "::", "..",
                ">=", "<=", "==", "!=", "&&", "||",
                "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=",
                "<<", ">>"
            };

            foreach (string op in operators)
            {
                if (index + op.Length <= source.Length &&
                    string.CompareOrdinal(source, index, op, 0, op.Length) == 0)
                    return op;
            }

            return null;
        }

        // -----------------------------------------------------------------
        // Parser
        // -----------------------------------------------------------------

        private sealed class Parser
        {
            private readonly List<Token> _tokens;
            private int _position;

            public Parser(List<Token> tokens)
            {
                _tokens = tokens;
            }

            public AstNode ParseProgram()
            {
                var root = new BlockNode();

                while (!AtEnd)
                {
                    int previousPosition = _position;
                    AstNode? statement = ParseStatement();

                    if (statement != null)
                        root.Children.Add(statement);

                    // Defensive progress guard: malformed or unsupported
                    // syntax must never leave the parser in an infinite loop.
                    if (_position == previousPosition)
                        _position++;
                }

                return root;
            }

            private bool AtEnd => _position >= _tokens.Count;

            private string Current =>
                AtEnd ? string.Empty : _tokens[_position].Value;

            private bool Is(string value) =>
                !AtEnd && _tokens[_position].Value == value;

            private AstNode? ParseStatement()
            {
                if (AtEnd || Is("}"))
                    return null;

                if (Is(";"))
                {
                    _position++;
                    return null;
                }

                if (Is("fn"))
                    return ParseFunction();

                if (Is("if"))
                    return ParseIf();

                if (Is("while"))
                    return ParseWhile();

                if (Is("for") || Is("loop"))
                    return ParseLoop();

                if (Is("match"))
                    return ParseMatch();

                // All remaining statements (let, assignment, return,
                // function/macro call, print, etc.) are simple CFG vertices.
                return ParseSimpleStatement();
            }

            private AstNode ParseFunction()
            {
                _position++; // fn

                // Skip the signature and attributes until the function body.
                while (!AtEnd && !Is("{") && !Is(";"))
                    _position++;

                var function = new FunctionNode();

                if (Is("{"))
                {
                    _position++;
                    function.Body = ParseBlock();
                }
                else if (Is(";"))
                {
                    _position++;
                }

                return function;
            }

            private AstNode ParseIf()
            {
                _position++; // if
                SkipToBlockStart();

                AstNode trueBranch = new BlockNode();
                if (Is("{"))
                {
                    _position++;
                    trueBranch = ParseBlock();
                }

                var node = new IfNode { TrueBranch = trueBranch };

                if (Is("else"))
                {
                    _position++;

                    if (Is("if"))
                        node.FalseBranch = ParseIf();
                    else if (Is("{"))
                    {
                        _position++;
                        node.FalseBranch = ParseBlock();
                    }
                    else if (!AtEnd)
                    {
                        // Tolerate an expression-form else branch.
                        node.FalseBranch = ParseSimpleStatement();
                    }
                }

                return node;
            }

            private AstNode ParseWhile()
            {
                _position++; // while
                SkipToBlockStart();

                var node = new WhileNode();
                if (Is("{"))
                {
                    _position++;
                    node.Body = ParseBlock();
                }

                return node;
            }

            private AstNode ParseLoop()
            {
                bool isFor = Is("for");
                _position++;

                if (isFor)
                {
                    // Skip the iterator expression.
                    SkipToBlockStart();
                }

                var node = new WhileNode();
                if (Is("{"))
                {
                    _position++;
                    node.Body = ParseBlock();
                }

                return node;
            }

            private void SkipToBlockStart()
            {
                int roundDepth = 0;
                int squareDepth = 0;

                while (!AtEnd)
                {
                    if (Is("("))
                        roundDepth++;
                    else if (Is(")"))
                        roundDepth = Math.Max(0, roundDepth - 1);
                    else if (Is("["))
                        squareDepth++;
                    else if (Is("]"))
                        squareDepth = Math.Max(0, squareDepth - 1);
                    else if (Is("{") && roundDepth == 0 && squareDepth == 0)
                        return;

                    _position++;
                }
            }

            private BlockNode ParseBlock()
            {
                var block = new BlockNode();

                while (!AtEnd && !Is("}"))
                {
                    int previousPosition = _position;
                    AstNode? statement = ParseStatement();

                    if (statement != null)
                        block.Children.Add(statement);

                    if (_position == previousPosition)
                        _position++;
                }

                if (Is("}"))
                    _position++;

                return block;
            }

            private AstNode ParseMatch()
            {
                _position++; // match

                while (!AtEnd && !Is("{"))
                    _position++;

                if (!Is("{"))
                    return new BlockNode();

                _position++; // opening brace

                var arms = new List<(bool IsFallback, AstNode Body)>();

                while (!AtEnd && !Is("}"))
                {
                    while (Is(","))
                        _position++;

                    if (AtEnd || Is("}"))
                        break;

                    int patternStart = _position;

                    while (!AtEnd && !Is("=>") && !Is("}"))
                        _position++;

                    if (!Is("=>"))
                        break;

                    bool isFallback = IsCatchAllPattern(patternStart, _position);
                    _position++; // =>

                    AstNode armBody = ParseMatchArmBody();
                    arms.Add((isFallback, armBody));

                    if (Is(","))
                        _position++;
                }

                if (Is("}"))
                    _position++;

                // A wildcard/binding arm is the final else branch. It is
                // NOT another decision vertex. This is the key correction:
                // match counter { 0 => A, 1 => B, _ => C } contains two
                // match decisions, followed by the fallback branch.
                IfNode? firstDecision = null;
                IfNode? lastDecision = null;
                AstNode? fallback = null;

                foreach (var arm in arms)
                {
                    if (arm.IsFallback)
                    {
                        fallback = arm.Body;
                        break;
                    }

                    var decision = new IfNode { TrueBranch = arm.Body };

                    if (firstDecision == null)
                        firstDecision = decision;
                    else
                        lastDecision!.FalseBranch = decision;

                    lastDecision = decision;
                }

                if (firstDecision == null)
                    return fallback ?? new BlockNode();

                lastDecision!.FalseBranch = fallback;
                return firstDecision;
            }

            private bool IsCatchAllPattern(int start, int end)
            {
                int length = end - start;
                if (length == 1)
                {
                    string value = _tokens[start].Value;
                    if (value == "_")
                        return true;

                    // In Rust, an unqualified lowercase identifier in a
                    // pattern is a binding and therefore matches anything.
                    if (_tokens[start].Kind == TokenKind.Identifier &&
                        value != "true" && value != "false" &&
                        value.Length > 0 && char.IsLower(value[0]))
                        return true;
                }

                return false;
            }

            private AstNode ParseMatchArmBody()
            {
                if (Is("{"))
                {
                    _position++;
                    return ParseBlock();
                }

                if (Is("if"))
                    return ParseIf();

                if (Is("while"))
                    return ParseWhile();

                if (Is("match"))
                    return ParseMatch();

                // Expression-style arm, e.g. `0 => "zero"`.
                // It is one simple vertex. Consume only this arm expression;
                // do not swallow the comma or the following arm.
                int roundDepth = 0;
                int squareDepth = 0;
                int braceDepth = 0;
                bool consumed = false;

                while (!AtEnd)
                {
                    if (Is(",") && roundDepth == 0 &&
                        squareDepth == 0 && braceDepth == 0)
                        break;

                    if (Is("}") && roundDepth == 0 &&
                        squareDepth == 0 && braceDepth == 0)
                        break;

                    if (Is("(")) roundDepth++;
                    else if (Is(")")) roundDepth = Math.Max(0, roundDepth - 1);
                    else if (Is("[")) squareDepth++;
                    else if (Is("]")) squareDepth = Math.Max(0, squareDepth - 1);
                    else if (Is("{")) braceDepth++;
                    else if (Is("}")) braceDepth = Math.Max(0, braceDepth - 1);

                    _position++;
                    consumed = true;
                }

                return consumed ? new SimpleNode() : new BlockNode();
            }

            private AstNode? ParseSimpleStatement()
            {
                int roundDepth = 0;
                int squareDepth = 0;
                int braceDepth = 0;
                bool consumed = false;

                while (!AtEnd)
                {
                    if (Is(";") && roundDepth == 0 &&
                        squareDepth == 0 && braceDepth == 0)
                    {
                        _position++;
                        break;
                    }

                    if (Is("}") && roundDepth == 0 &&
                        squareDepth == 0 && braceDepth == 0)
                        break;

                    if (Is("(")) roundDepth++;
                    else if (Is(")")) roundDepth = Math.Max(0, roundDepth - 1);
                    else if (Is("[")) squareDepth++;
                    else if (Is("]")) squareDepth = Math.Max(0, squareDepth - 1);
                    else if (Is("{")) braceDepth++;
                    else if (Is("}")) braceDepth = Math.Max(0, braceDepth - 1);

                    _position++;
                    consumed = true;
                }

                return consumed ? new SimpleNode() : null;
            }
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        public static Result Analyze(string code)
        {
            if (code == null)
                throw new ArgumentNullException(nameof(code));

            AstNode root = new Parser(Tokenize(code)).ParseProgram();

            int n = root.CountNodes();
            int sa = root.SumComplexities();

            double so = sa > 0 && n > 1
                ? 1.0 - ((double)(n - 1) / sa)
                : 0.0;

            return new Result
            {
                Sa = sa,
                So = so,
                N = n
            };
        }
    }
}
