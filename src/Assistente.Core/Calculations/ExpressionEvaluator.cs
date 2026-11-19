using System.Globalization;

namespace Assistente.Core.Calculations;

/// <summary>
/// Avaliador de expressões simples: + - * / ^ %, parênteses e menos unário.
/// Aceita vírgula ou ponto como separador decimal e os símbolos × ÷ −.
/// "%" pós-fixado divide por 100 (ex.: 50% = 0,5).
/// </summary>
public static class ExpressionEvaluator
{
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new FormatException("Expressão vazia.");

        var normalized = expression
            .Replace(',', '.')
            .Replace('×', '*')
            .Replace('÷', '/')
            .Replace('−', '-');

        var parser = new Parser(normalized);
        var value = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd) throw new FormatException($"Caractere inesperado '{parser.Current}' na posição {parser.Position + 1}.");
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArithmeticException("Resultado indefinido.");
        return value;
    }

    public static bool TryEvaluate(string expression, out double value)
    {
        try
        {
            value = Evaluate(expression);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArithmeticException)
        {
            value = 0;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        public int Position { get; private set; }

        public Parser(string s) => _s = s;

        public bool AtEnd => Position >= _s.Length;
        public char Current => _s[Position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) Position++;
        }

        private bool Accept(char c)
        {
            SkipWhitespace();
            if (!AtEnd && Current == c)
            {
                Position++;
                return true;
            }
            return false;
        }

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            var v = ParseTerm();
            while (true)
            {
                if (Accept('+')) v += ParseTerm();
                else if (Accept('-')) v -= ParseTerm();
                else return v;
            }
        }

        // term := unary (('*' | '/') unary)*
        private double ParseTerm()
        {
            var v = ParseUnary();
            while (true)
            {
                if (Accept('*')) v *= ParseUnary();
                else if (Accept('/'))
                {
                    var d = ParseUnary();
                    if (d == 0) throw new DivideByZeroException("Divisão por zero.");
                    v /= d;
                }
                else return v;
            }
        }

        // unary := ('-' | '+') unary | power
        private double ParseUnary()
        {
            if (Accept('-')) return -ParseUnary();
            if (Accept('+')) return ParseUnary();
            return ParsePower();
        }

        // power := postfix ('^' unary)?   (associativo à direita)
        private double ParsePower()
        {
            var b = ParsePostfix();
            return Accept('^') ? System.Math.Pow(b, ParseUnary()) : b;
        }

        // postfix := primary '%'*
        private double ParsePostfix()
        {
            var v = ParsePrimary();
            while (Accept('%')) v /= 100.0;
            return v;
        }

        private double ParsePrimary()
        {
            if (Accept('('))
            {
                var v = ParseExpression();
                if (!Accept(')')) throw new FormatException("Parêntese ')' esperado.");
                return v;
            }

            SkipWhitespace();
            var start = Position;
            while (!AtEnd && (char.IsAsciiDigit(Current) || Current == '.')) Position++;
            if (start == Position)
                throw new FormatException(AtEnd ? "Expressão incompleta." : $"Caractere inesperado '{Current}' na posição {Position + 1}.");

            var token = _s[start..Position];
            if (!double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"Número inválido: {token}");
            return number;
        }
    }
}
