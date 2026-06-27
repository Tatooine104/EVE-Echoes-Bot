using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region NodeStatus

/// <summary>
/// Возможные статусы выполнения узла дерева поведения.
/// </summary>
public enum NodeStatus
{
    /// <summary>Узел успешно завершил свою работу.</summary>
    Success,
    /// <summary>Узел не смог выполнить задачу или условие не выполнено.</summary>
    Failure,
    /// <summary>Узел всё еще находится в процессе выполнения (например, ожидание анимации UI).</summary>
    Running
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region BehaviorNode

/// <summary>
/// Абстрактный базовый класс для всех узлов дерева поведения.
/// </summary>
public abstract class BehaviorNode
{
    /// <summary>Имя узла для удобной отладки и логирования процессов.</summary>
    public string Name { get; protected set; } = "BaseNode";

    /// <summary>
    /// Главный асинхронный метод выполнения логики узла.
    /// </summary>
    public abstract Task<NodeStatus> TickAsync(ActiveBotAccount bot, CancellationToken token);
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region SequenceNode : BehaviorNode

/// <summary>
/// Композитный узел «Последовательность». Выполняет дочерние узлы один за другим.
/// Если хоть один дочерний узел возвращает Failure или Running, Sequence прекращает выполнение и возвращает этот статус.
/// Возвращает Success только если ВСЕ дети завершились успешно.
/// </summary>
public class SequenceNode : BehaviorNode
{
    private readonly List<BehaviorNode> _children = [];
    private int _currentChildIndex = 0;

    public SequenceNode(string name, params BehaviorNode[] nodes)
    {
        Name = name;
        _children.AddRange(nodes);
    }

    public override async Task<NodeStatus> TickAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Logger.Log($"[BT-TRACE] >>> Вход в Sequence: '{Name}' (Текущий индекс: {_currentChildIndex}/{_children.Count})", LogType.Test);

        for (int i = _currentChildIndex; i < _children.Count; i++)
        {
            var child = _children[i];
            // Logger.Log($"[BT-TRACE]  └─ [{Name}] Вызываю узел [{i}]: '{child.Name}'...", LogType.Test);

            NodeStatus childStatus = await child.TickAsync(bot, token).ConfigureAwait(false);

            // Logger.Log($"[BT-TRACE]  └─ [{Name}] Узел [{i}]: '{child.Name}' ВЕРНУЛ -> {childStatus}", LogType.Test);

            if (childStatus == NodeStatus.Running)
            {
                _currentChildIndex = i;
                // Logger.Log($"[BT-TRACE] <<< Выход из Sequence: '{Name}' со статусом RUNNING на шаге {i}", LogType.Test);
                return NodeStatus.Running;
            }

            if (childStatus == NodeStatus.Failure)
            {
                _currentChildIndex = 0;
                // Logger.Log($"[BT-TRACE] <<< Выход из Sequence: '{Name}' со статусом FAILURE на шаге {i} (Цепочка прервана, индекс сброшен)", LogType.Test);
                return NodeStatus.Failure;
            }
        }

        _currentChildIndex = 0;
        // Logger.Log($"[BT-TRACE] <<< Выход из Sequence: '{Name}' со статусом SUCCESS (Все шаги успешно пройдены)", LogType.Test);
        return NodeStatus.Success;
    }
}

public class SelectorNode : BehaviorNode
{
    private readonly List<BehaviorNode> _children = [];

    public SelectorNode(string name, params BehaviorNode[] nodes)
    {
        Name = name;
        _children.AddRange(nodes);
    }

    public override async Task<NodeStatus> TickAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[BT-TRACE] >>> Вход в Selector: '{Name}' (Всего детей: {_children.Count})", LogType.Test);

        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            Logger.Log($"[BT-TRACE]  ├─ [{Name}] Опрашиваю ветку [{i}]: '{child.Name}'...", LogType.Test);

            NodeStatus childStatus = await child.TickAsync(bot, token).ConfigureAwait(false);

            Logger.Log($"[BT-TRACE]  ├─ [{Name}] Ветка [{i}]: '{child.Name}' ВЕРНУЛ -> {childStatus}", LogType.Test);

            if (childStatus == NodeStatus.Success || childStatus == NodeStatus.Running)
            {
                Logger.Log($"[BT-TRACE] <<< Выход из Selector: '{Name}' со статусом {childStatus} (Ветка [{i}] подошла)", LogType.Test);
                return childStatus;
            }
        }

        Logger.Log($"[BT-TRACE] <<< Выход из Selector: '{Name}' со статусом FAILURE (Ни одна ветка не сработала)", LogType.Test);
        return NodeStatus.Failure;
    }
}


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region ActionNode : BehaviorNode

/// <summary>
/// Листовой узел действия. Выполняет переданную в него функцию (делегат).
/// </summary>
public class ActionNode : BehaviorNode
{
    private readonly Func<ActiveBotAccount, CancellationToken, Task<NodeStatus>> _action;

    public ActionNode(string name, Func<ActiveBotAccount, CancellationToken, Task<NodeStatus>> action)
    {
        Name = name;
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public override Task<NodeStatus> TickAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return _action(bot, token);
    }
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -