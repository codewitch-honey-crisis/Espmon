namespace HWKit;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

public enum HardwareInfoExpressionNodeType
{
    Terminal,
    Unary,
    Binary,
    NAry
}
public interface IHardwareInfoExpressionNode
{
    HardwareInfoExpressionNodeType NodeType { get; }
    IDictionary<object, object> UserData { get; }
    IHardwareInfoExpressionNonTerminalNode? Parent { get; }
    IReadOnlyList<IHardwareInfoExpressionNonTerminalNode> Ancestors { get; }
    IHardwareInfoExpressionNode? Next { get; }
    IHardwareInfoExpressionNode? Previous { get; }
    IReadOnlyList<IHardwareInfoExpressionNode> Predecessors { get; }
    IReadOnlyList<IHardwareInfoExpressionNode> Successors { get; }
}
public interface IHardwareInfoExpressionTerminalNode : IHardwareInfoExpressionNode
{
}
public interface IHardwareInfoExpressionNonTerminalNode : IHardwareInfoExpressionNode
{

    IReadOnlyList<IHardwareInfoExpressionNode> Children { get; }
    IReadOnlyList<IHardwareInfoExpressionNode> Descendants { get; }
    //void SetParents();
}
public interface IHardwareInfoExpressionUnaryNode : IHardwareInfoExpressionNonTerminalNode
{
    IHardwareInfoExpressionNode Child { get; }
}
public interface IHardwareInfoExpressionBinaryNode : IHardwareInfoExpressionNonTerminalNode
{
    IHardwareInfoExpressionNode LeftChild { get; }
    IHardwareInfoExpressionNode RightChild { get; }
}
public interface IHardwareInfoExpressionNAryNode : IHardwareInfoExpressionNonTerminalNode
{
    // inherited from base
    // but behavior is such that this list is stored, not computed from child node positions
    // IReadOnlyList<IHardwareInfoExpressionNode> Children { get; }
}

partial class HardwareInfoExpression : IHardwareInfoExpressionNode
{
    Lazy<IList<IHardwareInfoExpressionNonTerminalNode>> _ancestors;
    Lazy<IList<IHardwareInfoExpressionNode>> _predecessors;
    Lazy<IList<IHardwareInfoExpressionNode>> _successors;
    HardwareInfoExpressionNodeType IHardwareInfoExpressionNode.NodeType => GetNodeType();
    public HardwareInfoNonTerminalExpression? Parent { get; set; } = null;
    IHardwareInfoExpressionNonTerminalNode? IHardwareInfoExpressionNode.Parent { get { return Parent; } }
    IReadOnlyList<IHardwareInfoExpressionNonTerminalNode> IHardwareInfoExpressionNode.Ancestors => (IReadOnlyList < IHardwareInfoExpressionNonTerminalNode > )_ancestors.Value;
    IHardwareInfoExpressionNode? IHardwareInfoExpressionNode.Next => this.GetNext();
    IHardwareInfoExpressionNode? IHardwareInfoExpressionNode.Previous => this.GetPrevious();
    IReadOnlyList<IHardwareInfoExpressionNode> IHardwareInfoExpressionNode.Predecessors => (IReadOnlyList<IHardwareInfoExpressionNode>)_predecessors.Value;
    IReadOnlyList<IHardwareInfoExpressionNode> IHardwareInfoExpressionNode.Successors => (IReadOnlyList<IHardwareInfoExpressionNode>)_successors.Value;
    IDictionary<object, object> IHardwareInfoExpressionNode.UserData { get; } = new Dictionary<object, object>();

    protected abstract HardwareInfoExpressionNodeType GetNodeType();
}

partial class HardwareInfoTerminalExpression : IHardwareInfoExpressionTerminalNode
{
    protected override HardwareInfoExpressionNodeType GetNodeType()
    {
        return HardwareInfoExpressionNodeType.Terminal;
    }
}
partial class HardwareInfoNonTerminalExpression : IHardwareInfoExpressionNonTerminalNode
{
    Lazy<IList<IHardwareInfoExpressionNode>> _children;
    IReadOnlyList<IHardwareInfoExpressionNode> IHardwareInfoExpressionNonTerminalNode.Children
    {
        get
        {
            return (IReadOnlyList<IHardwareInfoExpressionNode>)_children.Value;
        }
    }
    Lazy<IList<IHardwareInfoExpressionNode>> _descendants;
    IReadOnlyList<IHardwareInfoExpressionNode> IHardwareInfoExpressionNonTerminalNode.Descendants
    {
        get
        {
            return (IReadOnlyList<IHardwareInfoExpressionNode>)_descendants.Value;
        }
    }
}
partial class HardwareInfoUnaryExpression : IHardwareInfoExpressionUnaryNode
{
    IHardwareInfoExpressionNode IHardwareInfoExpressionUnaryNode.Child => Expression;
    protected override HardwareInfoExpressionNodeType GetNodeType()
    {
        return HardwareInfoExpressionNodeType.Unary;
    }
}
partial class HardwareInfoBinaryExpression : IHardwareInfoExpressionBinaryNode
{
    IHardwareInfoExpressionNode IHardwareInfoExpressionBinaryNode.LeftChild => Left;
    IHardwareInfoExpressionNode IHardwareInfoExpressionBinaryNode.RightChild => Right;

    protected override HardwareInfoExpressionNodeType GetNodeType()
    {
        return HardwareInfoExpressionNodeType.Binary;
    }
}
partial class HardwareInfoNAryExpression : IHardwareInfoExpressionNAryNode
{
    IReadOnlyList<IHardwareInfoExpressionNode> IHardwareInfoExpressionNonTerminalNode.Children => Children;
    protected override HardwareInfoExpressionNodeType GetNodeType()
    {
        return HardwareInfoExpressionNodeType.NAry;
    }
}

/// <summary>
/// Indicates an action to take when a node is visited
/// </summary>
/// <param name="parent">The parent node</param>
/// <param name="expression">The current expression</param>
/// <param name="childIndex">The index of the expression within the parent</param>
/// <param name="level">The nexting level</param>
/// <returns>True to continue, otherwise false to exit visitation</returns>

public delegate bool HardwareInfoExpressionVisitAction<T>(T? parent, T expression, int childIndex, int level) where T : IHardwareInfoExpressionNode;

public static class HardwareInfoExpressionNodeHelpers
{


    public static IEnumerable<T> GetChildren<T>(this IHardwareInfoExpressionNode node) where T : IHardwareInfoExpressionNode
    {
        switch (node.NodeType)
        {
            case HardwareInfoExpressionNodeType.Unary:
                yield return (T)((IHardwareInfoExpressionUnaryNode)node).Child;
                yield break;
            case HardwareInfoExpressionNodeType.Binary:
                var bin = (IHardwareInfoExpressionBinaryNode)node;
                yield return (T)bin.LeftChild;
                yield return (T)bin.RightChild;
                yield break;
            case HardwareInfoExpressionNodeType.NAry:
                var nary = (IHardwareInfoExpressionNAryNode)node;
                for (int i = 0; i < nary.Children.Count; ++i)
                {
                    yield return (T)nary.Children[i];
                }
                yield break;
        }
    }
    public static IList<T> ToLazyNodeList<T>(this IEnumerable<T> inner) where T : IHardwareInfoExpressionNode
    {
        if (inner is IIndexedEnumerable<T> indexed)
        {
            return new IndexedEnumeratorList<T>(indexed);
        }
        else
        {
            return new IndexedEnumeratorList<T>(new IndexedEnumerableAdapter<T>(inner));
        }
    }
    public static IEnumerable<T> GetDescendants<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        return new IndexedEnumerableAdapter<T>(_GetDescendantsInner(current, false));
    }
    public static IEnumerable<T> GetDescendantsAndSelf<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        return new IndexedEnumerableAdapter<T>(_GetDescendantsInner(current, true));
    }
    static IEnumerable<T> _GetDescendantsInner<T>(T current, bool includeSelf) where T : IHardwareInfoExpressionNode
    {
        var stack = new Stack<(T current, T? parent, int childIndex, int level)>();
        stack.Push((current, default(T), 0, 0));
        if (includeSelf) yield return current;
        while (stack.Count > 0)
        {
            var (node, parent, childIndex, level) = stack.Pop();
            if (!object.ReferenceEquals(node, current))
            {
                yield return node;
            }

            // Push children in reverse order for preorder traversal
            switch (node.NodeType)
            {
                case HardwareInfoExpressionNodeType.NAry:
                    var nary = (IHardwareInfoExpressionNAryNode)node;
                    var children = nary.Children.ToArray();
                    for (int i = children.Length - 1; i >= 0; i--)
                    {
                        stack.Push(((T)children[i], node, i, level + 1));
                    }
                    break;

                case HardwareInfoExpressionNodeType.Unary:
                    var child = ((IHardwareInfoExpressionUnaryNode)node).Child;
                    stack.Push(((T)child, node, 0, level + 1));
                    break;

                case HardwareInfoExpressionNodeType.Binary:
                    var binary = (IHardwareInfoExpressionBinaryNode)node;
                    // Push right first, then left (so left gets processed first)
                    stack.Push(((T)binary.RightChild, node, 1, level + 1));
                    stack.Push(((T)binary.LeftChild, node, 0, level + 1));
                    break;
            }
        }
    }
    public static IEnumerable<T> GetAncestors<T>(this IHardwareInfoExpressionNode node) where T : IHardwareInfoExpressionNonTerminalNode
    {
        return new IndexedEnumerableAdapter<T>(_GetAncestorsInner<T>(node));
    }
    static IEnumerable<T> _GetAncestorsInner<T>(IHardwareInfoExpressionNode node) where T : IHardwareInfoExpressionNonTerminalNode
    {
        var parent = node.Parent;
        while (parent != null)
        {
            yield return (T)parent;
        }
    }
    public static T? GetNext<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        // First, try to go to the first child (if any)
        switch (current.NodeType)
        {
            case HardwareInfoExpressionNodeType.Unary:
                var child = ((IHardwareInfoExpressionUnaryNode)current).Child;
                if (child != null)
                    return (T)child;
                break;
            case HardwareInfoExpressionNodeType.Binary:
                var binary = (IHardwareInfoExpressionBinaryNode)current;
                if (binary.LeftChild != null)
                    return (T)binary.LeftChild;
                if (binary.RightChild != null)
                    return (T)binary.RightChild;
                break;
            case HardwareInfoExpressionNodeType.NAry:
                var nary = (IHardwareInfoExpressionNAryNode)current;
                throw new NotSupportedException("N-ary traversal is not supported by this operation");
        }

        // No children, so we need to go up and find the next sibling or ancestor's sibling
        var node = current;
        var parent = node.Parent;
        while (parent != null)
        {
            switch (parent.NodeType)
            {
                case HardwareInfoExpressionNodeType.Unary:
                    // Unary only has one child, so no next sibling
                    break;
                case HardwareInfoExpressionNodeType.Binary:
                    var binary = (IHardwareInfoExpressionBinaryNode)parent;
                    if (object.ReferenceEquals(node, binary.LeftChild) && !object.ReferenceEquals(binary.RightChild, null))
                        return (T)binary.RightChild;
                    // If we came from right child (or right is null), no next sibling
                    break;
                case HardwareInfoExpressionNodeType.NAry:
                    throw new NotSupportedException("N-ary traversal is not supported by this operation");
            }

            // No next sibling, go up one level
            node = (T)parent;
            parent = node.Parent;
        }

        return default; // Reached root with no next node
    }
    public static T? GetPrevious<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        var parent = current.Parent;
        if (parent == null)
            return default; // At root

        switch (parent.NodeType)
        {
            case HardwareInfoExpressionNodeType.Unary:
                // For unary, no previous sibling, parent is previous
                return (T)parent;

            case HardwareInfoExpressionNodeType.Binary:
                var binary = (IHardwareInfoExpressionBinaryNode)parent;
                if (object.ReferenceEquals(current, binary.RightChild) && !object.ReferenceEquals(binary.LeftChild, null))
                {
                    // Go to the rightmost descendant of left sibling
                    return _GetLast((T)binary.LeftChild);
                }
                // If we're the left child (or left is null), parent is previous
                return (T)parent;
            case HardwareInfoExpressionNodeType.NAry:
                throw new NotSupportedException("N-ary tree traversal is not supported by this operation");
            default:
                return (T)parent;
        }
    }

    private static T _GetLast<T>(T current) where T : IHardwareInfoExpressionNode
    {
        T node = current;
        while (true)
        {
            switch (node.NodeType)
            {
                case HardwareInfoExpressionNodeType.Unary:
                    var child = ((IHardwareInfoExpressionUnaryNode)node).Child;
                    if (!object.ReferenceEquals(child, null))
                    {
                        node = (T)child;
                        continue;
                    }
                    break;

                case HardwareInfoExpressionNodeType.Binary:
                    var binary = (IHardwareInfoExpressionBinaryNode)node;
                    if (!object.ReferenceEquals(binary.RightChild, null))
                    {
                        node = (T)binary.RightChild;
                        continue;
                    }
                    if (!object.ReferenceEquals(binary.LeftChild, null))
                    {
                        node = (T)binary.LeftChild;
                        continue;
                    }
                    break;
            }

            return node;
        }
    }
    public static IEnumerable<T> GetPredecessors<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        var node = current;
        while (!object.ReferenceEquals(node, null))
        {
            yield return node;
            node = node.GetPrevious();
        }
    }
    public static IEnumerable<T> GetSuccessors<T>(this T current) where T : IHardwareInfoExpressionNode
    {
        var node = current;
        while (node != null)
        {
            yield return node;
            node = GetNext(node);
        }
    }
    /// <summary>
    /// Visits nodes in the tree
    /// </summary>
    /// <typeparam name="T">The type of tree element</typeparam>
    /// <param name="current">The node to visit</param>
    /// <param name="action">The <see cref="HardwareInfoExpressionVisitAction{T}"/></param>
    /// <returns>True if all nodes were visited, or false if exited early</returns>
    [DebuggerHidden] // having this show up actually makes debugging more difficult
    public static bool Visit<T>(this T current, HardwareInfoExpressionVisitAction<T> action) where T : IHardwareInfoExpressionNode
    {
        var stack = new Stack<(T current, T? parent, int childIndex, int level)>();
        stack.Push((current, default(T), 0, 0));

        while (stack.Count > 0)
        {
            var (node, parent, childIndex, level) = stack.Pop();

            if (!action(parent, node, childIndex, level))
            {
                return false;
            }

            // Push children in reverse order for preorder traversal
            switch (node.NodeType)
            {
                case HardwareInfoExpressionNodeType.NAry:
                    var nary = (IHardwareInfoExpressionNAryNode)node;
                    var children = nary.Children.ToArray();
                    for (int i = children.Length - 1; i >= 0; i--)
                    {
                        stack.Push(((T)children[i], node, i, level + 1));
                    }
                    break;

                case HardwareInfoExpressionNodeType.Unary:
                    var child = ((IHardwareInfoExpressionUnaryNode)node).Child;
                    stack.Push(((T)child, node, 0, level + 1));
                    break;

                case HardwareInfoExpressionNodeType.Binary:
                    var binary = (IHardwareInfoExpressionBinaryNode)node;
                    // Push right first, then left (so left gets processed first)
                    stack.Push(((T)binary.RightChild, node, 1, level + 1));
                    stack.Push(((T)binary.LeftChild, node, 0, level + 1));
                    break;
            }
        }

        return true;
    }
    #region Enumerable implemeentations
    class _DescendantsEnumerator<T> : IIndexedEnumerator<T> where T : IHardwareInfoExpressionNode
    {
        const int _Disposed = -3;
        const int _BeforeStart = -2;
        const int _AfterEnd = -1;
        const int _First = 0;

        private readonly Stack<(T Node, int ChildIndex)> _stack;
        private readonly IHardwareInfoExpressionNode _root;
        private int _state;
        int _index;
        public _DescendantsEnumerator(IHardwareInfoExpressionNode root)
        {
            _root = root;
            _stack = new();
            _state = _BeforeStart;
            _index = -1;
        }

        private void _CheckState()
        {
            switch (_state)
            {
                case _BeforeStart:
                    throw new InvalidOperationException("The enumerator has not been started.");
                case _AfterEnd:
                    throw new InvalidOperationException("The enumerator is already finished.");
                case _Disposed:
                    throw new ObjectDisposedException(nameof(_DescendantsEnumerator<T>));
            }
        }

        public T Current
        {
            get
            {
                _CheckState();
                return _stack.Peek().Node;
            }
        }
        object? IEnumerator.Current { get { return Current; } }

        public int Index { get { return _index; } }

        KeyValuePair<int, T> IEnumerator<KeyValuePair<int, T>>.Current
        {
            get
            {
                _CheckState();
                return new KeyValuePair<int, T>(_index, _stack.Peek().Node);
            }
        }
        T IEnumerator<T>.Current { get => (T)Current; }
        public bool MoveNext()
        {
            switch (_state)
            {
                case _Disposed:
                    throw new ObjectDisposedException(nameof(_DescendantsEnumerator<T>));
                case _BeforeStart:
                    // Start with root's children (descendants only, not self)
                    if (_root == null || _root.NodeType == HardwareInfoExpressionNodeType.Terminal)
                    {
                        _state = _AfterEnd;
                        return false;
                    }
                    if (_root.NodeType == HardwareInfoExpressionNodeType.Unary)
                    {
                        var child = ((IHardwareInfoExpressionUnaryNode)_root).Child;
                        if (child != null)
                        {
                            _stack.Push(((T)child, 0));
                        }
                    }
                    else if (_root.NodeType == HardwareInfoExpressionNodeType.Binary)
                    {
                        var bin = (IHardwareInfoExpressionBinaryNode)_root;
                        // Push in reverse order for left-to-right processing
                        if (bin.RightChild != null)
                            _stack.Push(((T)bin.RightChild, 0));
                        if (bin.LeftChild != null)
                            _stack.Push(((T)bin.LeftChild, 0));
                    }

                    if (_stack.Count == 0)
                    {
                        _state = _AfterEnd;
                        return false;
                    }

                    _state = _First;
                    return MoveNext();
                case _AfterEnd:
                    return false;
            }

            // Main traversal logic
            while (_stack.Count > 0)
            {
                var (expr, childIndex) = _stack.Pop();

                switch (expr.NodeType)
                {
                    case HardwareInfoExpressionNodeType.Unary:
                        if (childIndex == 0)
                        {
                            _stack.Push((expr, 1)); // Mark as "child processed"
                            var child = ((IHardwareInfoExpressionUnaryNode)expr).Child;
                            if (child != null)
                            {
                                _stack.Push(((T)child, 0));
                                ++_index;
                                return true;
                            }
                        }
                        break;

                    case HardwareInfoExpressionNodeType.Binary:
                        var bin = (IHardwareInfoExpressionBinaryNode)expr;
                        if (childIndex == 0)
                        {
                            _stack.Push((expr, 1)); // Mark as "left child processed"
                            if (bin.LeftChild != null)
                            {
                                _stack.Push(((T)bin.LeftChild, 0));
                                ++_index;
                                return true;
                            }
                        }
                        else if (childIndex == 1)
                        {
                            _stack.Push((expr, 2)); // Mark as "both children processed"
                            if (bin.RightChild != null)
                            {
                                _stack.Push(((T)bin.RightChild, 0));
                                ++_index;
                                return true;
                            }
                        }
                        break;

                    default:
                        if (childIndex == 0)
                        {
                            // Leaf node - just return it
                            _stack.Push((expr, 1)); // Mark as "child processed"
                            ++_index;
                            return true;
                        }
                        break;
                }
            }

            _state = _AfterEnd;
            return false;
        }

        public void Reset()
        {
            if (_state == _Disposed)
                throw new ObjectDisposedException(nameof(_DescendantsEnumerator<T>));
            _index = -1;
            _stack.Clear();
            _state = _BeforeStart;
        }

        private void _Dispose(bool disposing)
        {
            if (_state != _Disposed)
            {
                if (disposing)
                {
                    _stack.Clear();
                }
                _state = _Disposed;
            }
        }

        ~_DescendantsEnumerator()
        {
            _Dispose(disposing: false);
        }

        void IDisposable.Dispose()
        {
            _Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    class _DescendantsEnumerable<T> : IIndexedEnumerable<T> where T : IHardwareInfoExpressionNode
    {
        IHardwareInfoExpressionNode _node;
        public _DescendantsEnumerable(IHardwareInfoExpressionNode expression)
        {
            _node = expression;
        }
        IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator()
        {
            return (IEnumerator<KeyValuePair<int, T>>)GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        IIndexedEnumerator<T> IIndexedEnumerable<T>.GetEnumerator()
        {
            return (IIndexedEnumerator<T>)GetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new _DescendantsEnumerator<T>(_node);
        }

    }
    #endregion // Enumerable implemeentations
    #region List implementations
    class _DescendantsList<T> : IndexedEnumeratorList<T> where T : IHardwareInfoExpressionNode
    {
        IHardwareInfoExpressionNode _root;
        public _DescendantsList(IHardwareInfoExpressionNode root) : base(new _DescendantsEnumerable<T>(root))
        {
            _root = root;
        }

        protected override bool ItemEquals(T? x, T? y)
        {
            return object.ReferenceEquals(x, y);
        }
    }
    class _AncestorsList<T> : IndexedEnumeratorList<T> where T : IHardwareInfoExpressionNonTerminalNode
    {
        IHardwareInfoExpressionNode _root;
        public _AncestorsList(IHardwareInfoExpressionNode root, IEnumerable<T> ancestors) : base(new IndexedEnumerableAdapter<T>(ancestors))
        {

            _root = root;
        }

        protected override bool ItemEquals(T? x, T? y)
        {
            return object.ReferenceEquals(x, y);
        }
    }
    #endregion
}