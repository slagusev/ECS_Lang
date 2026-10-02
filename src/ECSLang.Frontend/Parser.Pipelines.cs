using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private StageAction ParseParallelAction()
    {
        var parTok = Match(TokenType.Parallel);
        bool isAuto = false;
        if (Check(TokenType.Auto) || (Check(TokenType.Identifier) && Peek(0).Text == "auto"))
        {
            Advance();
            isAuto = true;
        }

        Match(TokenType.OpenBrace, isAuto ? "Expected '{' after 'parallel auto'." : "Expected '{' after 'parallel'.");
        var parallelSystems = new List<SystemCallAction>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var sNameTok = Match(TokenType.Identifier, "Expected system name in parallel block.");
            var sFullName = sNameTok.Text;
            if (Check(TokenType.Less))
            {
                Advance(); // <
                var typeArgs = new List<string>();
                do
                {
                    typeArgs.Add(ParseTypeAnnotation());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                Match(TokenType.Greater, "Expected '>' to close generic system type.");
                sFullName = $"{sFullName}<{string.Join(", ", typeArgs)}>";
            }
            Match(TokenType.Semicolon, "Expected ';' after system name.");
            parallelSystems.Add(new SystemCallAction(sFullName, sNameTok.Span));
        }
        Match(TokenType.CloseBrace, "Expected '}' after parallel block.");

        return isAuto
            ? new ParallelAutoBlockNode(parallelSystems, parTok.Span)
            : new ParallelAction(parallelSystems, parTok.Span);
    }
}
