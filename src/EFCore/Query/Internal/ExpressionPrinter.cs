// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class ExpressionPrinter : ExpressionVisitorBase, IExpressionPrinter
    {
        private readonly IndentedStringBuilder _stringBuilder;
        private readonly List<ConstantPrinterBase> _constantPrinters;
        private readonly Dictionary<ParameterExpression, string> _parametersInScope;

        private readonly Dictionary<ExpressionType, string> _binaryOperandMap = new Dictionary<ExpressionType, string>
        {
            { ExpressionType.Assign, " = " },
            { ExpressionType.Equal, " == " },
            { ExpressionType.NotEqual, " != " },
            { ExpressionType.GreaterThan, " > " },
            { ExpressionType.GreaterThanOrEqual, " >= " },
            { ExpressionType.LessThan, " < " },
            { ExpressionType.LessThanOrEqual, " <= " },
            { ExpressionType.OrElse, " || " },
            { ExpressionType.AndAlso, " && " },
            { ExpressionType.Coalesce, " ?? " },
            { ExpressionType.Add, " + " },
            { ExpressionType.Subtract, " - " },
            { ExpressionType.Multiply, " * " },
            { ExpressionType.Divide, " * " },
            { ExpressionType.Modulo, " % " },
            { ExpressionType.And, " & " },
            { ExpressionType.Or, " | " }
        };

        private bool _highlightNonreducibleNodes;

        private const string HighlightLeft = " ---> ";
        private const string HighlightRight = " <--- ";

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public ExpressionPrinter()
            : this(new List<ConstantPrinterBase>())
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected ExpressionPrinter(List<ConstantPrinterBase> constantPrinters)
        {
            _stringBuilder = new IndentedStringBuilder();
            _parametersInScope = new Dictionary<ParameterExpression, string>();
            _constantPrinters = new List<ConstantPrinterBase>(constantPrinters);
            _constantPrinters.AddRange(
                new List<ConstantPrinterBase>
                {
                    new EntityQueryableConstantPrinter(),
                    new CollectionConstantPrinter(),
                    new MetadataPropertyPrinter(),
                    new DefaultConstantPrinter()
                });
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IndentedStringBuilder StringBuilder => _stringBuilder;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool RemoveFormatting { get; set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual int? CharacterLimit { get; set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void Append([NotNull] string message) => _stringBuilder.Append(message);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void AppendLine([NotNull] string message = "")
        {
            if (RemoveFormatting)
            {
                _stringBuilder.Append(string.IsNullOrEmpty(message) ? " " : message);
            }

            _stringBuilder.AppendLine(message);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual string Print(Expression expression, bool removeFormatting = false, int? characterLimit = null)
        {
            return PrintInternal(expression, removeFormatting, characterLimit, highlightNonreducibleNodes: false);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual string PrintDebug(Expression expression, bool highlightNonreducibleNodes = true)
        {
            return PrintInternal(
                expression,
                removeFormatting: false,
                characterLimit: null,
                highlightNonreducibleNodes: highlightNonreducibleNodes);
        }

        private string PrintInternal(
            Expression expression,
            bool removeFormatting,
            int? characterLimit,
            bool highlightNonreducibleNodes)
        {
            _stringBuilder.Clear();
            _parametersInScope.Clear();

            RemoveFormatting = removeFormatting;
            CharacterLimit = characterLimit;
            _highlightNonreducibleNodes = highlightNonreducibleNodes;

            Visit(expression);

            var queryPlan = PostProcess(_stringBuilder.ToString());

            var result = "TRACKED: " + TrackedQuery + (removeFormatting ? " " : Environment.NewLine);
            result += queryPlan;

            if (characterLimit != null && characterLimit.Value > 0)
            {
                result = result.Length > characterLimit
                    ? result.Substring(0, characterLimit.Value) + "..."
                    : result;
            }

            return result;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool TrackedQuery { get; private set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Expression Visit(Expression expression)
        {
            if (expression == null)
            {
                return null;
            }

            if (CharacterLimit != null && _stringBuilder.Length > CharacterLimit.Value)
            {
                return expression;
            }

            switch (expression.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.ArrayIndex:
                case ExpressionType.Assign:
                case ExpressionType.Equal:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.NotEqual:
                case ExpressionType.OrElse:
                case ExpressionType.Coalesce:
                case ExpressionType.Add:
                case ExpressionType.Subtract:
                case ExpressionType.Multiply:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.Or:
                    VisitBinary((BinaryExpression)expression);
                    break;

                case ExpressionType.Block:
                    VisitBlock((BlockExpression)expression);
                    break;

                case ExpressionType.Conditional:
                    VisitConditional((ConditionalExpression)expression);
                    break;

                case ExpressionType.Constant:
                    VisitConstant((ConstantExpression)expression);
                    break;

                case ExpressionType.Lambda:
                    base.Visit(expression);
                    break;

                case ExpressionType.Goto:
                    VisitGoto((GotoExpression)expression);
                    break;

                case ExpressionType.Label:
                    VisitLabel((LabelExpression)expression);
                    break;

                case ExpressionType.MemberAccess:
                    VisitMember((MemberExpression)expression);
                    break;

                case ExpressionType.MemberInit:
                    VisitMemberInit((MemberInitExpression)expression);
                    break;

                case ExpressionType.Call:
                    VisitMethodCall((MethodCallExpression)expression);
                    break;

                case ExpressionType.New:
                    VisitNew((NewExpression)expression);
                    break;

                case ExpressionType.NewArrayInit:
                    VisitNewArray((NewArrayExpression)expression);
                    break;

                case ExpressionType.Parameter:
                    VisitParameter((ParameterExpression)expression);
                    break;

                case ExpressionType.Convert:
                case ExpressionType.Throw:
                case ExpressionType.Not:
                    VisitUnary((UnaryExpression)expression);
                    break;

                case ExpressionType.Default:
                    VisitDefault((DefaultExpression)expression);
                    break;

                case ExpressionType.Try:
                    VisitTry((TryExpression)expression);
                    break;

                case ExpressionType.Extension:
                    VisitExtension(expression);
                    break;

                default:
                    UnhandledExpressionType(expression);
                    break;
            }

            return expression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            Visit(binaryExpression.Left);

            if (binaryExpression.NodeType == ExpressionType.ArrayIndex)
            {
                _stringBuilder.Append("[");

                Visit(binaryExpression.Right);

                _stringBuilder.Append("]");
            }
            else
            {
                if (!_binaryOperandMap.TryGetValue(binaryExpression.NodeType, out string operand))
                {
                    UnhandledExpressionType(binaryExpression);
                }
                else
                {
                    _stringBuilder.Append(operand);
                }

                Visit(binaryExpression.Right);
            }

            return binaryExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitBlock(BlockExpression blockExpression)
        {
            AppendLine();
            AppendLine("{");
            _stringBuilder.IncrementIndent();

            foreach (var variable in blockExpression.Variables)
            {
                if (!_parametersInScope.ContainsKey(variable))
                {
                    _parametersInScope.Add(variable, "var" + _parametersInScope.Count);
                }
            }

            var expressions = blockExpression.Result != null
                ? blockExpression.Expressions.Except(new[] { blockExpression.Result })
                : blockExpression.Expressions;

            foreach (var expression in expressions)
            {
                Visit(expression);
                AppendLine();
            }

            if (blockExpression.Result != null)
            {
                Append("return ");
                Visit(blockExpression.Result);
            }

            _stringBuilder.DecrementIndent();
            AppendLine("}");

            return blockExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitConditional(ConditionalExpression conditionalExpression)
        {
            Visit(conditionalExpression.Test);

            _stringBuilder.Append(" ? ");

            Visit(conditionalExpression.IfTrue);

            _stringBuilder.Append(" : ");

            Visit(conditionalExpression.IfFalse);

            return conditionalExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitConstant(ConstantExpression constantExpression)
        {
            foreach (var constantPrinter in _constantPrinters)
            {
                if (constantPrinter.TryPrintConstant(constantExpression, _stringBuilder, RemoveFormatting))
                {
                    break;
                }
            }

            return constantExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitGoto(GotoExpression gotoExpression)
        {
            AppendLine("return (" + gotoExpression.Target.Type.ShortDisplayName() + ")" + gotoExpression.Target + " {");
            _stringBuilder.IncrementIndent();

            Visit(gotoExpression.Value);

            _stringBuilder.DecrementIndent();
            _stringBuilder.Append("}");

            return gotoExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitLabel(LabelExpression labelExpression)
        {
            _stringBuilder.Append(labelExpression.Target.ToString());

            return labelExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitLambda<T>(Expression<T> lambdaExpression)
        {
            _stringBuilder.Append("(");

            foreach (var parameter in lambdaExpression.Parameters)
            {
                if (!_parametersInScope.ContainsKey(parameter))
                {
                    _parametersInScope.Add(parameter, parameter.Name);
                }

                _stringBuilder.Append(parameter.Type.ShortDisplayName() + " " + parameter.Name);

                if (parameter != lambdaExpression.Parameters.Last())
                {
                    _stringBuilder.Append(" | ");
                }
            }

            _stringBuilder.Append(") => ");

            Visit(lambdaExpression.Body);

            foreach (var parameter in lambdaExpression.Parameters)
            {
                _parametersInScope.Remove(parameter);
            }

            return lambdaExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            if (memberExpression.Expression != null)
            {
                Visit(memberExpression.Expression);
            }
            else
            {
                // ReSharper disable once PossibleNullReferenceException
                _stringBuilder.Append(memberExpression.Member.DeclaringType.Name);
            }

            _stringBuilder.Append("." + memberExpression.Member.Name);

            return memberExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitMemberInit(MemberInitExpression memeberInitExpression)
        {
            _stringBuilder.Append("new " + memeberInitExpression.Type.ShortDisplayName());

            var appendAction = memeberInitExpression.Bindings.Count > 1 ? (Action<string>)AppendLine : Append;
            appendAction("{ ");
            _stringBuilder.IncrementIndent();

            for (var i = 0; i < memeberInitExpression.Bindings.Count; i++)
            {
                if (memeberInitExpression.Bindings[i] is MemberAssignment assignment)
                {
                    _stringBuilder.Append(assignment.Member.Name + " = ");
                    Visit(assignment.Expression);
                    appendAction(i == memeberInitExpression.Bindings.Count - 1 ? " " : ", ");
                }
                else
                {
                    ////throw new NotSupportedException(CoreStrings.InvalidMemberInitBinding);
                    AppendLine(CoreStrings.InvalidMemberInitBinding);
                }
            }

            _stringBuilder.DecrementIndent();
            AppendLine("}");

            return memeberInitExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            var simpleMethods = new List<string>
            {
                "get_Item",
                "TryReadValue"
            };

            switch (methodCallExpression.Method.Name)
            {
                case "_InterceptExceptions":
                    Visit(methodCallExpression.Arguments[0]);

                    return methodCallExpression;
                case "_TrackEntities":
                    TrackedQuery = true;
                    Visit(methodCallExpression.Arguments[0]);

                    return methodCallExpression;
            }

            if (!EntityQueryModelVisitor.IsPropertyMethod(methodCallExpression.Method))
            {
                _stringBuilder.Append(methodCallExpression.Method.ReturnType.ShortDisplayName() + " ");
            }

            if (methodCallExpression.Object != null)
            {
                Visit(methodCallExpression.Object);
                _stringBuilder.Append(".");
            }

            _stringBuilder.Append(methodCallExpression.Method.Name + "(");

            var appendAction
                = simpleMethods.Contains(methodCallExpression.Method.Name)
                  || EntityQueryModelVisitor.IsPropertyMethod(methodCallExpression.Method)
                    ? (Action<string>)Append
                    : AppendLine;

            if (methodCallExpression.Arguments.Count > 0)
            {
                appendAction("");

                var showArgumentNames
                    = !simpleMethods.Contains(methodCallExpression.Method.Name)
                      && !EntityQueryModelVisitor.IsPropertyMethod(methodCallExpression.Method);
                var argumentNames
                    = showArgumentNames
                        ? methodCallExpression.Method.GetParameters().Select(p => p.Name).ToList()
                        : new List<string>();

                _stringBuilder.IncrementIndent();
                for (var i = 0; i < methodCallExpression.Arguments.Count; i++)
                {
                    var argument = methodCallExpression.Arguments[i];

                    if (showArgumentNames)
                    {
                        _stringBuilder.Append(argumentNames[i] + ": ");
                    }

                    Visit(argument);

                    appendAction(i == methodCallExpression.Arguments.Count - 1 ? "" : ", ");
                }

                _stringBuilder.DecrementIndent();
            }

            appendAction(")");

            return methodCallExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitNew(NewExpression newExpression)
        {
            _stringBuilder.Append("new ");
            _stringBuilder.Append(newExpression.Type.ShortDisplayName());

            var appendAction = newExpression.Arguments.Count > 1 ? (Action<string>)AppendLine : Append;
            appendAction("(");
            _stringBuilder.IncrementIndent();

            for (var i = 0; i < newExpression.Arguments.Count; i++)
            {
                Visit(newExpression.Arguments[i]);
                appendAction(i == newExpression.Arguments.Count - 1 ? "" : ", ");
            }

            _stringBuilder.DecrementIndent();
            _stringBuilder.Append(")");

            return newExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitNewArray(NewArrayExpression newArrayExpression)
        {
            var appendAction = newArrayExpression.Expressions.Count > 1 ? (Action<string>)AppendLine : Append;
            appendAction("new " + newArrayExpression.Type.GetElementType().ShortDisplayName() + "[]");
            appendAction("{ ");
            _stringBuilder.IncrementIndent();

            for (var i = 0; i < newArrayExpression.Expressions.Count; i++)
            {
                Visit(newArrayExpression.Expressions[i]);
                appendAction(i == newArrayExpression.Expressions.Count - 1 ? " " : ", ");
            }

            _stringBuilder.DecrementIndent();
            AppendLine("}");

            return newArrayExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            if (_parametersInScope.ContainsKey(parameterExpression))
            {
                _stringBuilder.Append(_parametersInScope[parameterExpression]);
            }
            else
            {
                _stringBuilder.Append("Unhandled parameter: " + parameterExpression);
            }

            return parameterExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitUnary(UnaryExpression unaryExpression)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (unaryExpression.NodeType)
            {
                case ExpressionType.Convert:
                    _stringBuilder.Append("(" + unaryExpression.Type.ShortDisplayName() + ") ");
                    Visit(unaryExpression.Operand);
                    break;

                case ExpressionType.Throw:
                    _stringBuilder.Append("throw ");
                    Visit(unaryExpression.Operand);
                    break;

                case ExpressionType.Not:
                    _stringBuilder.Append("!(");
                    Visit(unaryExpression.Operand);
                    _stringBuilder.Append(")");
                    break;

                default:
                    UnhandledExpressionType(unaryExpression);
                    break;
            }

            return unaryExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitDefault(DefaultExpression defaultExpression)
        {
            _stringBuilder.Append("default(" + defaultExpression.Type + ")");

            return defaultExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitTry(TryExpression tryExpression)
        {
            _stringBuilder.Append("try { ");
            Visit(tryExpression.Body);
            _stringBuilder.Append(" } ");

            foreach (var handler in tryExpression.Handlers)
            {
                _stringBuilder.Append("catch (" + handler.Test.Name + ") { ... } ");
            }

            return tryExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitExtension(Expression extensionExpression)
        {
            if (_highlightNonreducibleNodes && !extensionExpression.CanReduce)
            {
                StringBuilder.Append(HighlightLeft);
            }

            switch (extensionExpression)
            {
                case QuerySourceReferenceExpression qsre:
                    StringBuilder.Append(qsre);
                    break;

                case NullConditionalExpression nullConditional:
                    StringBuilder.Append(nullConditional.ToString());
                    break;

                default:
                    UnhandledExpressionType(extensionExpression);
                    break;
            }

            if (_highlightNonreducibleNodes && !extensionExpression.CanReduce)
            {
                StringBuilder.Append(HighlightRight);
            }

            return extensionExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual string PostProcess([NotNull] string queryPlan)
        {
            var processedPlan = queryPlan
                .Replace("Microsoft.EntityFrameworkCore.Query.", "")
                .Replace("Microsoft.EntityFrameworkCore.", "")
                .Replace(Environment.NewLine + Environment.NewLine, Environment.NewLine);

            return processedPlan;
        }

        private void UnhandledExpressionType(Expression expression)
            => AppendLine(expression.ToString());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected abstract class ConstantPrinterBase
        {
            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            public abstract bool TryPrintConstant(
                [NotNull] ConstantExpression constantExpression,
                [NotNull] IndentedStringBuilder stringBuilder,
                bool removeFormatting);

            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            protected virtual Action<IndentedStringBuilder, string> Append => (sb, s) => sb.Append(s);

            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            protected virtual Action<IndentedStringBuilder, string> AppendLine => (sb, s) => sb.AppendLine(s);
        }

        private class EntityQueryableConstantPrinter : ConstantPrinterBase
        {
            public override bool TryPrintConstant(
                ConstantExpression constantExpression,
                IndentedStringBuilder stringBuilder,
                bool removeFormatting)
            {
                if (constantExpression.IsEntityQueryable())
                {
                    stringBuilder.Append($"DbSet<{constantExpression.Type.GetTypeInfo().GenericTypeArguments.First().ShortDisplayName()}>");
                    return true;
                }

                return false;
            }
        }

        private class CollectionConstantPrinter : ConstantPrinterBase
        {
            public override bool TryPrintConstant(
                ConstantExpression constantExpression,
                IndentedStringBuilder stringBuilder,
                bool removeFormatting)
            {
                var value = constantExpression.Value;
                if (value is IEnumerable enumerable
                    && !(value is string))
                {
                    var appendAction = value is byte[] || removeFormatting ? Append : AppendLine;

                    appendAction(stringBuilder, value.GetType().ShortDisplayName() + " ");
                    appendAction(stringBuilder, "{ ");
                    stringBuilder.IncrementIndent();
                    foreach (var item in enumerable)
                    {
                        appendAction(stringBuilder, item + ", ");
                    }

                    stringBuilder.DecrementIndent();
                    appendAction(stringBuilder, "}");

                    return true;
                }

                return false;
            }
        }

        private class MetadataPropertyPrinter : ConstantPrinterBase
        {
            public override bool TryPrintConstant(
                ConstantExpression constantExpression,
                IndentedStringBuilder stringBuilder,
                bool removeFormatting)
            {
                if (constantExpression.Value is Property property)
                {
                    stringBuilder.Append(property.Name);

                    return true;
                }

                return false;
            }
        }

        private class DefaultConstantPrinter : ConstantPrinterBase
        {
            public override bool TryPrintConstant(
                ConstantExpression constantExpression,
                IndentedStringBuilder stringBuilder,
                bool removeFormatting)
            {
                var stringValue = "null";
                var value = constantExpression.Value;

                if (value != null)
                {
                    stringValue = value.ToString() != value.GetType().ToString()
                        ? value.ToString()
                        : value.GetType().Name;

                    if (value is string)
                    {
                        stringValue = $@"""{stringValue}""";
                    }
                }

                stringBuilder.Append(stringValue);

                return true;
            }
        }
    }
}
