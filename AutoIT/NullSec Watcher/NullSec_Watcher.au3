; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Watcher                                                                                            #
; # VERSION........: 1.0.0                                                                                                      #
; # DESCRIPTION....: Автономный модуль мониторинга локального чата и оповещения в альянс.                                       #
; #                                                                                                                             #
; ###############################################################################################################################

#include <WinAPIFiles.au3>
#include <WinAPISys.au3>
#include "..\Resource\libUtility.au3"
#include "..\Resource\libImageSearch.au3"
#include "..\Resource\libGUI.au3"
#include "..\Resource\libEVEEchoesFuncs.au3" ; 

; 1. ИНИЦИАЛИЗАЦИЯ
Global $sResourceDir = _WinAPI_GetFullPathName(@ScriptDir & "\..\Resource\") & "\"
Global $Debug = True
Global $aIsSave = [True]
Global $iCurrentClient = 0

; 2. ПОДГОТОВКА ОКНА
; Функция сама найдет окно MEmu, подгонит размер и вернет его Handle
Local $hClient = _Util_PrepareClient("MEmu.exe", 1280, 720)

If $hClient = 0 Then Exit ; Сообщение об ошибке уже будет в логе внутри функции

Global $aClients = [$hClient]

; 3. ЗАПУСК ИНТЕРФЕЙСА И ЦИКЛА
_GUI_Init("Watcher Mode", 10, 10)

_Log("Watcher запущен. Ожидание нажатия СТАРТ...")

; 3. ОСНОВНОЙ ЦИКЛ
While 1
    _GUI_Update() ; Обновляем время работы в GUI

    If $bIsRunning Then ; Если нажата кнопка "Старт" в GUI
        Local $sTarget = $aClients[$iCurrentClient]

        ; ШАГ 1: Проверка безопасности
        ; Передаем имя окна и индекс (0), результат запишется в $aIsSave (если массив) или $IsSave
        If Not _IsSafe($sTarget, $iCurrentClient) Then
            _Log("!!! ВНИМАНИЕ: Обнаружен нейтрал/враг в " & $sTarget)

            ; ШАГ 2: Отправка сообщения в чат Альянса
            If _AlliChatMessage($sTarget) Then
                _Log("Доклад в Альянс-чат отправлен успешно.")
            Else
                _Log("ОШИБКА: Не удалось отправить доклад.")
            EndIf

            ; Делаем паузу, чтобы не спамить чат каждую секунду
            _HumanSleep(30000, 60000)
        Else
            ; Если все чисто, просто ждем перед следующей проверкой
            _HumanSleep(2000, 5000)
        EndIf
    Else
        Sleep(200) ; Бот на паузе
    EndIf
WEnd
