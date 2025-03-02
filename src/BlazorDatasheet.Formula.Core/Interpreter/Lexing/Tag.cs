namespace BlazorDatasheet.Formula.Core.Interpreter.Lexing;

public enum Tag
{
    Eof,
    Number,
    BadToken,
    GreaterThanOrEqualToToken,
    LessThanOrEqualToToken,
    StringToken,
    CommaToken,
    PlusToken,
    StarToken,
    MinusToken,
    SlashToken,
    EqualsToken,
    IdentifierToken,
    NotEqualToToken,
    GreaterThanToken,
    SemiColonToken,
    BangToken,
    PercentToken,
    LessThanToken,
    LeftParenthToken,
    RightParenthToken,
    LeftCurlyBracketToken,
    RightCurlyBracketToken,
    AmpersandToken,
    ColonToken,
    AddressToken,
    LogicalToken,
    Whitespace,
    SheetLocatorToken
}