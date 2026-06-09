using Templar.Rendering;
using G = Atelier.Framework.Test.Generators.Templates.Operation;

namespace Atelier.Framework.Test.Generators;

internal static class OperationTestEmitter
{
    private static readonly Compositor ASYNC_UNWRAP = new G.AsyncUnwrap();

    public static IEnumerable<Compositor> Emit(ConsumerMetadata m)
    {
        if (m.Operations.Count == 0)
        {
            yield break;
        }
        if (m.RequisiteFields.Count == 0)
        {
            yield break;
        }

        if (!m.GeneratorEmitsConstructor)
        {
            foreach (var op in m.Operations)
            {
                yield return FixtureStub(m, op);
            }
            yield break;
        }

        var fqn = m.FullyQualifiedName;
        var arity = m.ExpectedCtorArity;
        var seen = new Dictionary<string, int>();

        foreach (var op in m.Operations)
        {
            var paramCount = op.Parameters.Count;
            var nameSeq = seen.TryGetValue(op.Name, out var c) ? c + 1 : 0;
            seen[op.Name] = nameSeq;
            var suffix = nameSeq == 0 ? op.Name : op.Name + "_" + nameSeq;
            var target = fqn + "." + op.Name + "(" + paramCount + ")";

            if (!op.ReturnsOutcomeShape)
            {
                yield return new G.ReturnShape
                {
                    Target = target,
                    Suffix = suffix,
                    OpName = op.Name,
                    ReturnType = op.FullyQualifiedReturnTypeName.Replace("\"", "\\\""),
                };
            }

            yield return new G.NoThrow
            {
                Target = target,
                AsyncKw = op.IsAsync ? "async Task " : "void ",
                Suffix = suffix,
                Preamble = Preamble(fqn, arity, op.Name, paramCount),
                AwaitBlock = op.IsAsync ? ASYNC_UNWRAP : null,
                OutcomeCheck = op.ReturnsOutcomeShape ? new G.NoThrowOutcomeCheck() : null,
            };

            if (op.ReturnsOutcomeShape)
            {
                var argOverrides = Sequence.Lines(op.Parameters
                    .Select((prm, idx) => (prm, idx))
                    .Where(t => t.prm.IsString)
                    .Select(t => (Compositor)new G.ArgOverride
                    {
                        Index = t.idx,
                        ValueExpr = "\"atelier-happy\"",
                    }));

                yield return new G.HappyPath
                {
                    Target = target,
                    AsyncKw = op.IsAsync ? "async Task " : "void ",
                    Suffix = suffix,
                    Preamble = Preamble(fqn, arity, op.Name, paramCount),
                    ArgOverrides = argOverrides,
                    AwaitBlock = op.IsAsync ? ASYNC_UNWRAP : null,
                };
            }

            var ctIndex = -1;
            for (var i = 0; i < op.Parameters.Count; i++)
            {
                if (op.Parameters[i].IsCancellationToken)
                {
                    ctIndex = i;
                    break;
                }
            }
            if (ctIndex >= 0)
            {
                yield return new G.Cancellation
                {
                    Target = target,
                    AsyncKw = op.IsAsync ? "async Task " : "void ",
                    Suffix = suffix,
                    Preamble = Preamble(fqn, arity, op.Name, paramCount),
                    CtIndex = ctIndex,
                    AwaitBlock = op.IsAsync ? ASYNC_UNWRAP : null,
                };
            }

            for (var pi = 0; pi < op.Parameters.Count; pi++)
            {
                var prm = op.Parameters[pi];
                if (!prm.IsNonNullableReference)
                {
                    continue;
                }
                yield return new G.NullParam
                {
                    Target = target,
                    AsyncKw = op.IsAsync ? "async Task " : "void ",
                    Suffix = suffix,
                    ParamIdent = SafeIdent(prm.Name),
                    Preamble = Preamble(fqn, arity, op.Name, paramCount),
                    ParamIndex = pi,
                    ParamName = prm.Name,
                    AwaitBlock = op.IsAsync ? ASYNC_UNWRAP : null,
                };
            }

            for (var pi = 0; pi < op.Parameters.Count; pi++)
            {
                var prm = op.Parameters[pi];
                if (!prm.IsString)
                {
                    continue;
                }
                yield return new G.EmptyString
                {
                    Target = target,
                    AsyncKw = op.IsAsync ? "async Task " : "void ",
                    Suffix = suffix,
                    ParamIdent = SafeIdent(prm.Name),
                    Preamble = Preamble(fqn, arity, op.Name, paramCount),
                    ParamIndex = pi,
                    ParamName = prm.Name,
                    AwaitBlock = op.IsAsync ? ASYNC_UNWRAP : null,
                    OutcomeCheck = op.ReturnsOutcomeShape
                        ? new G.EmptyStringOutcomeCheck { ParamName = prm.Name }
                        : null,
                };
            }

            yield return new G.Concurrent
            {
                Target = target,
                Suffix = suffix,
                Preamble = Preamble(fqn, arity, op.Name, paramCount),
            };
        }
    }

    private static Compositor Preamble(string fqn,
                                       int arity,
                                       string opName,
                                       int paramCount) =>
        new G.ResolvePreamble
        {
            Fqn = fqn,
            Arity = arity,
            OpName = opName,
            ParamCount = paramCount,
        };

    private static Compositor FixtureStub(ConsumerMetadata m, OperationMethod op) =>
        new G.FixtureStub
        {
            Target = m.FullyQualifiedName + "." + op.Name + "(" + op.Parameters.Count + ")",
            OpName = op.Name,
            ParamCount = op.Parameters.Count,
        };

    private static string SafeIdent(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var result = new string(chars);
        if (char.IsDigit(result[0]))
        {
            return "_" + result;
        }
        return result;
    }

}
