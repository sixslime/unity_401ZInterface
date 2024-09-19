using System.Collections;
using System.Collections.Generic;
using ControlledFlows;
using FourZeroOne.Resolution;
using FourZeroOne.Rule;
using FourZeroOne.Token.Unsafe;
using Perfection;
using UnityEngine;

namespace FourZeroOne.Runtimes.FrameSaving
{
    public class Gebug : Runtime.FrameSaving
    {
        private int depth = 0;
        private string depthPad => "--".Yield(depth).AccumulateInto("", (msg, x) => msg + x);
        private World _world;
        public Gebug(State startingState, IToken program, GameObject worldObject) : base(startingState, program)
        {
            _world = worldObject.AddComponent<World>();
        }

        protected override void RecieveFrame(LinkedStack<Frame> frameNode)
        {
            Debug.Log($"{depthPad}FRAME");
        }

        protected override void RecieveMacroExpansion(IToken macro, IToken expanded)
        {
            Debug.Log($"{depthPad}& {macro} -> {expanded}");
        }

        protected override void RecieveResolution(IOption<IResolution> resolution)
        {
            Debug.Log($"{depthPad}* {resolution}");
            depth--;
        }

        protected override void RecieveRuleSteps(IEnumerable<(IToken token, IRule appliedRule)> steps)
        {
            Debug.Log($"{depthPad}+ {steps.AccumulateInto("", (msg, x) => msg + x.token + $"\n")}");
        }

        protected override void RecieveToken(IToken token)
        {
            Debug.Log($"{depthPad}: {token}");
            depth++;
        }

        protected override ControlledFlow<IOption<IEnumerable<R>>> SelectionImplementation<R>(IEnumerable<R> from, int count)
        {
            throw new System.NotImplementedException();
        }

        private class World : MonoBehaviour
        {
            private ICeasableFlow<IOption<List<R>>> SelectionLogic<R>(IEnumerable<R> outOf, int count)
            {
                var o = new List<R>(count);
                if (0 >= count) return ControlledFlow.Resolved(new None<List<R>>());
                var options = new List<(Renderer visual, R data)>();
                int index = 0;
                int _total = 0;

                var defaultColor = new Color(0.4f, 0.4f, 0.4f);
                var defaultSize = new Vector3(0.5f, 0.5f, 0.5f);
                foreach (var (i, v) in outOf.Enumerate())
                {
                    _total++;
                    var visual = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
                    visual.transform.localScale = defaultSize;
                    visual.transform.position = new Vector3(i * 0.8f - 2f, 0, 0);
                    visual.material.color = defaultColor;
                    options.Add((visual, v));
                }
                count = (count > _total) ? _total : count;
                var resolveOnAllSelected = new ControlledFlow<IOption<List<R>>>();
                var input = new Generated.TestInput();
                input.Selection.left.performed += _ => __Left();
                input.Selection.right.performed += _ => __Right();
                input.Selection.enter.performed += _ => __Select();
                input.Selection.cancel.performed += _ => __Cancel();
                input.Enable();
                __Hover();
                return resolveOnAllSelected.WithTransformedResult(x => { __Exit(); return x; });

                //Debug.Log($"SELECTED: {new PList<R>() { Elements = o.Or(new()) }}");

                void __Exit()
                {
                    foreach (var (visual, _) in options) Destroy(visual.gameObject);
                    input.Disable();
                    input.Dispose();
                }
                void __Left()
                {
                    options[index].visual.transform.localScale = defaultSize;
                    index = (index > 0) ? index - 1 : 0;
                    __Hover();
                }
                void __Right()
                {
                    options[index].visual.transform.localScale = defaultSize;
                    index = (_total > index) ? index + 1 : _total;
                    __Hover();
                }
                void __Hover()
                {
                    options[index].visual.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                    Debug.Log($"HOVER: {options[index].data}");
                }
                void __Select()
                {
                    var sel = options[index];
                    if (o.Remove(sel.data))
                    {
                        sel.visual.material.color = defaultColor;
                    }
                    else
                    {
                        sel.visual.material.color = new Color(0.4f, 0.8f, 0.4f);
                        o.Add(sel.data);
                        if (o.Count >= count) resolveOnAllSelected.Resolve(o.AsSome());
                    }
                }
                void __Cancel()
                {
                    o = null;
                    resolveOnAllSelected.Resolve(new None<List<R>>());
                }
            }
        }
    }
}