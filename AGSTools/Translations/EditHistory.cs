using System;
using System.Collections.Generic;
using System.Linq;

namespace AGSTools
{
    /// <summary>
    /// Undo and redo for text values of many items (the translations of a table). Typing in one item makes one
    /// step as long as the edits follow each other quickly; changes made between <see cref="BeginBatch"/> and
    /// <see cref="EndBatch"/> (e.g. auto-translating all empty entries) make one step together.
    /// </summary>
    public sealed class EditHistory<T> where T : class
    {
        private sealed class Change
        {
            public Change(T item, string before, string after)
            {
                Item = item;
                Before = before;
                After = after;
            }

            public T Item { get; }
            public string Before { get; }
            public string After { get; set; }
        }

        private sealed class Step
        {
            public Step(string? label) => Label = label;
            public string? Label { get; }
            public List<Change> Changes { get; } = new();
            public DateTime Time { get; set; }
        }

        private readonly LinkedList<Step> _undo = new();
        private readonly Stack<Step> _redo = new();
        private readonly int _limit;
        private readonly TimeSpan _typingPause;
        private Step? _batch;
        private int _batchDepth;

        /// <param name="limit">Steps kept; older ones are dropped.</param>
        /// <param name="typingPause">Edits of the same item closer together than this are one step.</param>
        public EditHistory(int limit = 500, TimeSpan? typingPause = null)
        {
            _limit = Math.Max(1, limit);
            _typingPause = typingPause ?? TimeSpan.FromSeconds(2);
        }

        /// <summary>True while <see cref="Undo"/> or <see cref="Redo"/> sets values; such changes are not recorded.</summary>
        public bool IsApplying { get; private set; }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>The batch label of the next undo step, or null for a single edit.</summary>
        public string? UndoLabel => _undo.Last?.Value.Label;
        public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

        /// <summary>Records that <paramref name="item"/> changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
        public void Record(T item, string before, string after, DateTime now)
        {
            if (IsApplying || before == after)
                return;
            _redo.Clear();
            if (_batch != null)
            {
                Merge(_batch, item, before, after);
                return;
            }
            Step? last = _undo.Last?.Value;
            if (last is { Label: null } && last.Changes.Count == 1 && last.Changes[0].Item == item && now - last.Time < _typingPause)
            {
                last.Changes[0].After = after;
                last.Time = now;
                return;
            }
            var step = new Step(null) { Time = now };
            step.Changes.Add(new Change(item, before, after));
            Push(step);
        }

        /// <summary>Starts a step that collects all changes until <see cref="EndBatch"/>; calls may nest.</summary>
        public void BeginBatch(string label)
        {
            if (_batchDepth++ == 0)
                _batch = new Step(label);
        }

        public void EndBatch()
        {
            if (_batchDepth == 0 || --_batchDepth > 0)
                return;
            Step batch = _batch!;
            _batch = null;
            if (batch.Changes.Count > 0)
                Push(batch);
        }

        /// <summary>Sets every item of the last step back; returns the changed items (the last one first), empty if none.</summary>
        public IReadOnlyList<T> Undo(Action<T, string> setValue)
        {
            if (_undo.Last == null)
                return Array.Empty<T>();
            Step step = _undo.Last.Value;
            _undo.RemoveLast();
            Apply(step.Changes.AsEnumerable().Reverse(), c => c.Before, setValue);
            _redo.Push(step);
            return step.Changes.Select(c => c.Item).Reverse().ToList();
        }

        /// <summary>Does the last undone step again; returns the changed items, empty if none.</summary>
        public IReadOnlyList<T> Redo(Action<T, string> setValue)
        {
            if (_redo.Count == 0)
                return Array.Empty<T>();
            Step step = _redo.Pop();
            Apply(step.Changes, c => c.After, setValue);
            step.Time = DateTime.MinValue; // typing after a redo starts a new step
            _undo.AddLast(step);
            return step.Changes.Select(c => c.Item).ToList();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _batch = null;
            _batchDepth = 0;
        }

        private void Apply(IEnumerable<Change> changes, Func<Change, string> value, Action<T, string> setValue)
        {
            IsApplying = true;
            try
            {
                foreach (Change change in changes)
                    setValue(change.Item, value(change));
            }
            finally
            {
                IsApplying = false;
            }
        }

        // The same item changed twice in a batch: undo goes back to its first value
        private static void Merge(Step step, T item, string before, string after)
        {
            Change? existing = step.Changes.FirstOrDefault(c => c.Item == item);
            if (existing != null)
                existing.After = after;
            else
                step.Changes.Add(new Change(item, before, after));
        }

        private void Push(Step step)
        {
            _undo.AddLast(step);
            while (_undo.Count > _limit)
                _undo.RemoveFirst();
        }
    }
}
