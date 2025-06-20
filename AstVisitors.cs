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

    //
    // NB: AST not used for callgraph generation. Keeping this visitor for potential future use.
    //
    internal class CallGraphVisitor : DepthFirstAstVisitor
    {
        public readonly List<CalledMethodModel> _calledMethods = new();

        public CallGraphVisitor(List<CalledMethodModel> calledMethods)
        {
            _calledMethods = calledMethods;
        }

        public override void VisitInvocationExpression(InvocationExpression invocation)
        {
            var target = invocation.Target.ToString();
            if (!_calledMethods.Any(m => m.Name == target))
            {
                _calledMethods.Add(new CalledMethodModel()
                {
                    Name = target,
                    Address = 0 // must be resolved later
                });
            }
            base.VisitInvocationExpression(invocation);
        }
    }
}
