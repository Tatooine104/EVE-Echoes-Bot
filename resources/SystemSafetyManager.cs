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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    #region Static Fields

    /// <summary>
    /// Потокобезопасный словарь, хранящий индивидуальные состояния безопасности для каждой звездной системы EVE Echoes.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SystemSafetyState> _systems = new(StringComparer.OrdinalIgnoreCase);

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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
