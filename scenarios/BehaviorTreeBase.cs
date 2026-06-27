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
    // ИСПРАВЛЕНО HIGH: Индекс для запоминания текущего выполняющегося шага цепочки
    private int _currentChildIndex = 0;

    public SequenceNode(string name, params BehaviorNode[] nodes)
    {
        Name = name;
        _children.AddRange(nodes);
    }

    public override async Task<NodeStatus> TickAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Начинаем обход строго с того узла, на котором остановились в прошлый раз!
        for (int i = _currentChildIndex; i < _children.Count; i++)
        {
            NodeStatus childStatus = await _children[i].TickAsync(bot, token);

            if (childStatus == NodeStatus.Running)
            {
                _currentChildIndex = i; // Запоминаем шаг, уходим на следующий секундный тик цикла
                return NodeStatus.Running;
            }

            if (childStatus == NodeStatus.Failure)
            {
                _currentChildIndex = 0; // Провал цепочки — сбрасываем память
                return NodeStatus.Failure;
            }
        }

        _currentChildIndex = 0; // Все узлы успешно пройдены — обнуляем индекс
        return NodeStatus.Success;
    }
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region SelectorNode : BehaviorNode

/// <summary>
/// Композитный узел «Селектор (Выбор)». Выполняет дочерние узлы по очереди, пока один из них не вернет Success или Running.
/// Прекращает работу и возвращает Success/Running сразу, как только наткнется на успешный узел.
/// Возвращает Failure только если абсолютно ВСЕ дети провалились.
/// </summary>
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
        foreach (var child in _children)
        {
            NodeStatus childStatus = await child.TickAsync(bot, token).ConfigureAwait(false);

            // Если ребенок вернул Success или Running — мы нашли решение, 
            // мгновенно возвращаем этот статус наверх и КУПИРУЕМ дальнейший обход!
            if (childStatus == NodeStatus.Success || childStatus == NodeStatus.Running)
            {
                return childStatus;
            }

            // ИСПРАВЛЕНО HIGH: Если ребенок вернул Failure (как это делает планетарка), 
            // мы НЕ ВЫХОДИМ из цикла, а послушно переходим к СЛЕДУЮЩЕМУ элементу foreach (к coreScenarioTree)!
#if DEBUG
            // Выводим отладку только в режиме разработки, чтобы видеть логику перескока веток
            // Logger.Log($"[Selector: {Name}] Узел '{child.Name}' вернул Failure. Перехожу к следующей ветке...", LogType.Test);
#endif
        }

        // Возвращаем Failure ТОЛЬКО если абсолютно все ветки (и планетарка, и основной скрипт) вернули неудачу
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