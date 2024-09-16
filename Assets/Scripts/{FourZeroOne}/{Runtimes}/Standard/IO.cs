using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using ControlledFlows;
using UnityEngine.InputSystem;
using Perfection;
using System;

#nullable enable
// very placeholder behavior.
// eventually seperate input and output provider.
// the input provider should just have a reference to the output provider to get selection objects.
namespace FourZeroOne.Runtimes.Standard
{
    using Token.Unsafe;
    using Token;
    using ResObj = Resolution.IResolution;
    public class IO : MonoBehaviour, IInputInterface, IOutputInterface
    {
        private int depth = 0;
        private string depthPad => "--".Yield(depth).AccumulateInto("", (msg, x) => msg + x);
        public ICeasableFlow<IOption<IEnumerable<R>>?> ReadSelection<R>(IEnumerable<R> outOf, int count) where R : class, ResObj
        {
            return SelectionLogic(outOf, count);
        }

        public void WriteRuleSteps(IEnumerable<(IToken fromToken, Rule.IRule rule)> pairs)
        {
            Debug.Log($"{depthPad}+ {pairs.AccumulateInto("", (msg, x) => msg + x.fromToken + $"\n")}");
        }

        public void WriteToken(IToken token)
        {
            Debug.Log($"{depthPad}: {token}");
            depth++;
        }
        public void WriteResolution(IOption<ResObj>? resolution)
        {
            Debug.Log($"{depthPad}* {resolution}");
            depth--;
        }
        /// <summary>
        /// Called whenever this runtime's <see cref="IRuntime.State"/> is updated, <paramref name="state"/> containing the new state.
        /// </summary>
        /// <param name="state"></param>
        public void WriteState(State state)
        {

        }

        // very temporary (obv), bare minumum to allow for selection token testing.
        private async ICeasableFlow<IOption<List<R>>> SelectionLogic<R>(IEnumerable<R> outOf, int count)
        {
            var o = new List<R>(count);
            if (0 >= count) return new None<List<R>>();
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
            var resolveOnAllSelected = new ControlledFlow.ControlledFlow();
            var input = new Generated.TestInput();
            input.Selection.left.performed += _ => __Left();
            input.Selection.right.performed += _ => __Right();
            input.Selection.enter.performed += _ => __Select();
            input.Selection.cancel.performed += _ => __Cancel();
            input.Enable();
            __Hover();

            await resolveOnAllSelected;
            foreach (var (visual, _) in options) Destroy(visual.gameObject);
            input.Disable();
            input.Dispose();
            //Debug.Log($"SELECTED: {new PList<R>() { Elements = o.Or(new()) }}");
            return o.AsSome();

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
                    if (o.Count >= count) resolveOnAllSelected.Resolve();
                }
            }
            void __Cancel()
            {
                o = null;
                resolveOnAllSelected.Resolve();
            }
        }


    }

}
