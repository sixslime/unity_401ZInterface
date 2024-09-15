
using System.Collections.Generic;
using Perfection;
using ControlledTasks;
using System.Threading.Tasks;
using static UnityEngine.Debug;
#nullable enable
namespace FourZeroOne.Runtime
{
    using ResObj = Resolution.IResolution;
    using Resolved = IOption<Resolution.IResolution>;
    using IToken = Token.Unsafe.IToken;
    using Token;
    public interface IRuntime
    {
        public State GetState();
        public ICeasableTask<IOption<R>> PerformAction<R>(IToken<R> action) where R : class, ResObj;
        public ICeasableTask<IOption<IEnumerable<R>>> ReadSelection<R>(IEnumerable<R> from, int count) where R : class, ResObj;
    }

    //garbage collector reliant/heavy implementation
    public abstract class FrameSaving : IRuntime
    {
        public FrameSaving(State startingState, IToken program)
        {
            _currentState = startingState;
            _operationStack = new LinkedStack<IToken>(program).AsSome();
            _resolutionStack = new None<LinkedStack<Resolved>>();
            _evalThread = ControlledTask.FromResult(new None<ResObj>());
            _runThread = ControlledTask.FromResult((Resolved)(new None<ResObj>()));
            _frameStack = new None<LinkedStack<Frame>>();
            AddFrame(program, new None<Resolved>());
        }
        public async Task<Resolved> Run()
        {
            _runThread = new ControlledTask<Resolved>();
            RunInternal();
            return await _runThread;
        }
        private void ResolveRun(Resolved resolution)
        {
            _runThread.Resolve(resolution);
        }
        public State GetState() => _currentState;
        public ICeasableTask<IOption<R>> PerformAction<R>(IToken<R> action) where R : class, ResObj
        {
            var node = _operationStack.Unwrap();
            if (node.Value is not Core.Tokens.PerformAction<R> pToken)
            {
                throw new System.Exception("[FrameSaving Runtime] PerformAction() called when a PerformAction token was not at the top of the operation stack.");
            }
            _operationStack = (node with
            {
                Value = pToken.Arg1
            }).AsSome();
            return pToken.Arg1.R
        }
        public ICeasableTask<IOption<IEnumerable<R>>> ReadSelection<R>(IEnumerable<R> from, int count) where R : class, ResObj
        {
            return SelectionImplementation(from, count);
        }

        protected abstract void RecieveToken(IToken token);
        protected abstract void RecieveResolution(IOption<ResObj> resolution);
        protected abstract void RecieveRuleSteps(IEnumerable<(IToken token, Rule.IRule appliedRule)> steps);
        protected abstract ControlledTask<IOption<IEnumerable<R>>> SelectionImplementation<R>(IEnumerable<R> from, int count) where R : class, ResObj;

        protected void GoToFrame(LinkedStack<Frame> frameStack)
        {
            var frame = frameStack.Value;
            _operationStack = frame.OperationStack;
            _resolutionStack = frame.ResolutionStack;
            _currentState = frame.State;
            _frameStack = frameStack.AsSome();
            _evalThread.Cease();
            RunInternal();
        }

        protected record Frame
        {
            public IToken Token { get; init; }
            public IOption<Resolved> Resolution { get; init; }
            public State State { get; init; }
            public IOption<LinkedStack<IToken>> OperationStack { get; init; }
            public IOption<LinkedStack<Resolved>> ResolutionStack { get; init; }
        }
        protected record LinkedStack<T>
        {
            public readonly IOption<LinkedStack<T>> Link;
            public readonly int Depth;
            public T Value { get; init; } 
            public LinkedStack(T value)
            {
                Value = value;
                Link = this.None();
                Depth = 0;
            }
            public static IOption<LinkedStack<T>> Linked(IOption<LinkedStack<T>> parent, int depth, IEnumerable<T> values)
            {
                return values.AccumulateInto(parent, (stack, x) => new LinkedStack<T>(stack, x, depth).AsSome());
            }
            public static IOption<LinkedStack<T>> Linked(IOption<LinkedStack<T>> parent, int depth, params T[] values) { return Linked(parent, depth, values.IEnumerable()); }
            private LinkedStack(IOption<LinkedStack<T>> link, T value, int depth)
            {
                Link = link;
                Value = value;
                Depth = depth;
            }
        }

        private async void RunInternal()
        {
            while (_operationStack.Check(out var unruledOperationNode))
            {
                var operationNode = unruledOperationNode with
                {
                    Value = ApplyRules(unruledOperationNode.Value, _currentState.Rules.Elements, out var appliedRules)
                };
                RecieveRuleSteps(appliedRules);
                RecieveToken(operationNode.Value);
                _operationStack = operationNode.AsSome();
                int argAmount = operationNode.Value.ArgTokens.Length;
                
                //DEV - each operation node should have a IOption<ControlledTask<Resolved>> attached that resolves upon resolution.
                if (argAmount == 0 || (_resolutionStack.Check(out var resolutionNode) && resolutionNode.Depth == operationNode.Depth + 1))
                {
                    var argPass = new Resolved[argAmount];
                    for (int i = argAmount; i >= 0; i--)
                    { 
                        argPass[i] = PopFromStack(ref _resolutionStack).Value;
                    }
                    _evalThread = operationNode.Value.ResolveUnsafe(this, argPass);
                    var resolution = await _evalThread;
                    RecieveResolution(resolution);
                    if (resolution.Check(out var notNolla)) _currentState = _currentState.WithResolution(notNolla);
                    PushToStack(ref _resolutionStack, operationNode.Depth, resolution);
                    PopFromStack(ref _operationStack);
                    AddFrame(operationNode.Value, resolution.AsSome());
                } else
                {
                    PushToStack(ref _operationStack, operationNode.Depth + 1, operationNode.Value.ArgTokens.AsMutList().Reversed());
                }
            }

            Assert(_resolutionStack.Check(out var finalNode) && !finalNode.Link.IsSome());
            ResolveRun(finalNode.Value);
        }
        private void AddFrame(IToken token, IOption<Resolved> resolution)
        {
            var frame = new Frame()
            {
                Resolution = resolution,
                Token = token,
                State = _currentState,
                OperationStack = _operationStack,
                ResolutionStack = _resolutionStack,
            };
            PushToStack(ref _frameStack, 0, frame);
        }
        private static void PushToStack<T>(ref IOption<LinkedStack<T>> stack, int depth, IEnumerable<T> values)
        {
            stack = LinkedStack<T>.Linked(stack, depth, values);
        }
        private static LinkedStack<T> PopFromStack<T>(ref IOption<LinkedStack<T>> stack)
        {
            var o = stack.Check(out var popped) ? popped : throw new System.Exception("[FrameSaving Runtime] tried to pop from empty LinkedStack.");
            if (stack.Check(out var node)) stack = node.Link;
            return o;
        }
        private static void PushToStack<T>(ref IOption<LinkedStack<T>> stack, int depth, params T[] values) { PushToStack(ref stack, depth, values.IEnumerable()); }
        private static IToken ApplyRules(IToken token, IEnumerable<Rule.IRule> rules, out List<(IToken fromToken, Rule.IRule rule)> appliedRules)
        {
            var o = token;
            appliedRules = new();
            foreach (var rule in rules)
            {
                if (rule.TryApply(o).Check(out var newToken))
                {
                    appliedRules.Add((o, rule));
                    o = newToken;
                }
            }
            return o;
        }

        private State _currentState;
        private ICeasableTask<Resolved> _evalThread;
        private ControlledTask<Resolved> _runThread;
        private IOption<LinkedStack<Frame>> _frameStack;
        private IOption<LinkedStack<IToken>> _operationStack;
        private IOption<LinkedStack<Resolved>> _resolutionStack;

    }
}