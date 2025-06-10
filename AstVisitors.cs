using ICSharpCode.Decompiler.CSharp.Syntax;

namespace AssemblyAnalyzer
{
    class StringLiteralVisitor : DepthFirstAstVisitor
    {
        private readonly List<string> _stringLiterals;

        public StringLiteralVisitor(List<string> stringLiterals)
        {
            _stringLiterals = stringLiterals;
        }

        public override void VisitPrimitiveExpression(PrimitiveExpression primitiveExpression)
        {
            if (primitiveExpression.Value is string stringValue)
            {
                _stringLiterals.Add(stringValue);
            }
            base.VisitPrimitiveExpression(primitiveExpression);
        }
    }
}
