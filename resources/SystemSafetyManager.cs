using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EVEEchoesBot.resources;

/// <summary>
/// Глобальный менеджер безопасности звездных систем.
/// Синглтон-хранилище, координирующее статусы угроз между всеми параллельно работающими потоками аккаунтов.
/// </summary>
public static class SystemSafetyManager
{

    private static readonly Lock _globalLock = new();

    /// <summary>
    /// Потокобезопасно устанавливает статус опасности для системы.
    /// </summary>
    /// <returns>True, если статус РЕАЛЬНО изменился с безопасного на опасный</returns>
    public static bool TrySetSystemDanger(string systemName)
    {
        // BUG HIGH - Потенциальный Lock Order Inversion Deadlock (Взаимная блокировка из-за нарушения порядка захвата локов). Внутри `lock (_globalLock)` ты вызываешь метод `GetSystemState(systemName)`, который лезет в `ConcurrentDictionary.GetOrAdd()`. Если в этот же момент словарь `_systems` под капотом расширяет свои корзины (resize), а другой поток запрашивает стейт, рантайм может устроить клин между внутренним локом словаря и твоим внешним `_globalLock`. Но самое страшное дальше: внутри этого же лока вызывается `state.IsSafe` и `state.SetDanger()`, которые захватывают ЛОКАЛЬНЫЙ `_lock` конкретного объекта `SystemSafetyState`. Если в каком-то другом файле (например, в логике паники бота или в эндпоинтах Kestrel) сначала захватывается локальный лок объекта состояния, а затем идет обращение к глобальному менеджеру, вы получите классический мертвый замок (Deadlock) на стыке первого же опасного тика. Метод `GetSystemState` необходимо вызывать ДО захвата `lock (_globalLock)`, а вложенные блокировки убрать.
        lock (_globalLock)
        {
            var state = GetSystemState(systemName);
            if (state.IsSafe is false)
                return false; // Сигнал уже обработан ранее, дублировать панику не нужно

            state.SetDanger(); // Переводим в статус Danger внутри вашего менеджера
            return true; // Статус реально изменился впервые
        }
    }

    public static void SetSystemSafe(string systemName)
    {
        // BUG HIGH - Дублирование риска дедлока. Вызов `GetSystemState` внутри `lock (_globalLock)` с последующим заходом в `state.SetSafe()`, который внутри себя захватывает `_lock` третьего уровня. Нарушается иерархия захвата ресурсов. Сначала нужно получить объект из словаря БЕЗ лока, а затем работать с его внутренним атомарным или изолированным локом. Внешний `_globalLock` здесь вообще избыточен, так как сам словарь `ConcurrentDictionary` уже потокобезопасен.
        lock (_globalLock)
        {
            var state = GetSystemState(systemName);
            state.SetSafe();
        }
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Static Fields

    /// <summary>
    /// Потокобезопасный словарь, хранящий индивидуальные состояния безопасности для каждой звездной системы EVE Echoes.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SystemSafetyState> _systems = new(StringComparer.OrdinalIgnoreCase);

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Public Methods

    /// <summary>
    /// Возвращает или создает уникальный объект состояния безопасности для указанной звездной системы.
    /// </summary>
    /// <param name="eveSystem">Название звездной системы (например, Jita).</param>
    /// <returns>Экземпляр <see cref="SystemSafetyState"/>, управляющий флагами угроз данной системы.</returns>
    public static SystemSafetyState GetSystemState(string eveSystem)
    {
        if (string.IsNullOrEmpty(eveSystem))
        {
            eveSystem = "Неизвестно";
        }

        return _systems.GetOrAdd(eveSystem, _ => new SystemSafetyState());
    }

    #endregion
}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

/// <summary>
/// Описывает текущее состояние безопасности конкретной звездной системы.
/// Инкапсулирует логику переключения триггеров паники и защиты от спама алертами альянса.
/// </summary>
public class SystemSafetyState
{
    #region Private Fields

    /// <summary>
    /// Высокоэффективный строго типизированный объект блокировки (введен в C# 13 / .NET 9).
    /// Минимизирует накладные расходы процессора при синхронизации потоков эмуляторов.
    /// </summary>
    private readonly Lock _lock = new();

    private bool? _isSafe = true;
    private bool _allianceAlertSent = false;

    #endregion

    #region Properties

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Текущий статус безопасности системы.
    /// <para>Значение <c>true</c> — в системе безопасно.</para>
    /// <para>Значение <c>false</c> — зафиксирован враг/нейтрал.</para>
    /// </summary>
    public bool? IsSafe
    {
        get { lock (_lock) return _isSafe; }
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Public Methods

    /// <summary>
    /// Переводит систему в состояние опасности.
    /// </summary>
    /// <returns>
    /// Возвращает <c>true</c>, если это первое обнаружение врага в текущем цикле опасности
    /// (сигнал для отправки макроса в чат альянса). Возвращает <c>false</c>, если оповещение уже было отправлено другим окном.
    /// </returns>
    public bool SetDanger()
    {
        // Синтаксис блокировки lock остается классическим, но благодаря объекту типа Lock компиляция идет через новые быстрые инструкции .NET 9
        lock (_lock)
        {
            _isSafe = false;

            if (!_allianceAlertSent)
            {
                _allianceAlertSent = true;
                return true; // Разрешаем текущему потоку-инициатору отправить алерт
            }

            return false; // Запрещаем дублирующие алерты из других окон в этой же системе
        }
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Сбрасывает состояние системы в режим «Безопасно» после того, как все угрозы покинули локал.
    /// Восстанавливает триггер отправки будущих боевых оповещений альянса.
    /// </summary>
    public void SetSafe()
    {
        lock (_lock)
        {
            _isSafe = true;
            _allianceAlertSent = false;
        }
    }

    #endregion
}
