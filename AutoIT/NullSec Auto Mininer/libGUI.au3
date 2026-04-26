#include-once
#include <GUIConstantsEx.au3>
#include <StaticConstants.au3>
#include <WindowsConstants.au3>
#include <libUtility.au3>        ; Содержит общий и служебные функции 


; #FUNCTION# ====================================================================================================================
; Name...........: _GUI_Init
; Description....: Создает расширенное окно управления ботом с динамическими параметрами, телеметрией и логом.
; Syntax.........: _GUI_Init($sTitle, $iX, $iY, $aParams)
; Parameters ....: $sTitle  - Заголовок окна.
;                  $iX      - Координата X появления окна.
;                  $iY      - Координата Y появления окна.
;                  $aParams - [Optional] Двумерный массив параметров [[Название, Значение], ...].
; Updated .......: 2026.04.26
; Version .......: 1.10
; Remarks .......: Автоматически подстраивает высоту окна под количество параметров.
; ===============================================================================================================================
Func _GUI_Init($sTitle = "Bot Control", $iX = 100, $iY = 100, $aParams = Default)
    Opt("GUIOnEventMode", 1) ; Включаем режим событий

    ; 1. Расчет динамической высоты
    Local $iParamCount = IsArray($aParams) ? UBound($aParams) : 0
    Local $iParamSectionHeight = ($iParamCount > 0) ? (25 + ($iParamCount * 20) + 10) : 0
    Local $iTotalHeight = 320 + $iParamSectionHeight

    Local $hGUI = GUICreate($sTitle, 400, $iTotalHeight, $iX, $iY, BitOR($WS_CAPTION, $WS_SYSMENU))
    GUISetOnEvent($GUI_EVENT_CLOSE, "_Terminate")

    ; --- Секция 1: Информация о скрипте ---
    GUICtrlCreateGroup(" Информация о скрипте ", 10, 10, 380, 60)
    GUICtrlCreateLabel("Скрипт: " & @ScriptName, 20, 30, 360, 20)
    GUICtrlSetFont(-1, 9, 800)

    ; --- Секция 2: Телеметрия ---
    GUICtrlCreateGroup(" Телеметрия ", 10, 75, 380, 100)
    Global $sStartTime = StringFormat("%02d:%02d:%02d", @HOUR, @MIN, @SEC)
    GUICtrlCreateLabel("Запуск: " & $sStartTime, 20, 95, 170, 20)
    
    Global $hGUI_Runtime = GUICtrlCreateLabel("Работает: 00:00:00", 200, 95, 180, 20)
    Global $iTimerStart = TimerInit()

    Global $hGUI_Target = GUICtrlCreateLabel("Цель: Ожидание...", 20, 120, 360, 20)
    
    Global $hStatusLabel = GUICtrlCreateLabel("Инициализация...", 20, 145, 360, 20)
    GUICtrlSetColor(-1, 0x0000FF) ; Синий цвет

    ; --- Секция 3: Динамические параметры (если переданы) ---
    Local $iYOffset = 180
    If $iParamCount > 0 Then
        GUICtrlCreateGroup(" Параметры конфигурации ", 10, $iYOffset, 380, ($iParamCount * 22) + 15)
        For $i = 0 To $iParamCount - 1
            ; Название параметра
            GUICtrlCreateLabel($aParams[$i][0] & ":", 20, $iYOffset + 20 + ($i * 20), 150, 20)
            ; Значение параметра
            GUICtrlCreateLabel($aParams[$i][1], 180, $iYOffset + 20 + ($i * 20), 200, 20)
            GUICtrlSetFont(-1, 9, 800)
        Next
        $iYOffset += ($iParamCount * 22) + 25 ; Смещаем следующую секцию ниже
    EndIf

    ; --- Секция 4: Последние события (LOG) ---
    GUICtrlCreateGroup(" Последние события (LOG) ", 10, $iYOffset, 380, 90)
    Global $hGUI_Log = GUICtrlCreateLabel("", 20, $iYOffset + 20, 360, 65, $SS_LEFT)
    Global $sLogBuffer = ""

    ; --- Кнопка СТОП ---
    Local $btnStop = GUICtrlCreateButton("СТОП (EXIT)", 10, $iYOffset + 100, 380, 30)
    GUICtrlSetOnEvent($btnStop, "_Terminate")
    GUICtrlSetBkColor(-1, 0xFFCCCC)

    GUISetState(@SW_SHOW)
    Return $hGUI
EndFunc   ;==>_GUI_Init


; #FUNCTION# ====================================================================================================================
; Name...........: _GUI_Update
; Description....: Обновляет динамические данные в интерфейсе (таймер работы и имя целевого окна).
; Syntax.........: _GUI_Update()
; Parameters ....: Нет
; Return values .: Нет
; Updated .......: 2026.04.26
; Version .......: 1.00
; Remarks .......: Должна вызываться внутри основного цикла While для актуализации времени работы.
; ===============================================================================================================================
Func _GUI_Update()

    ; 1. Обновление таймера работы
    Local $iDiff  = TimerDiff($iTimerStart) / 1000
    Local $iHours = Int($iDiff / 3600)
    Local $iMins  = Int(Mod($iDiff, 3600) / 60)
    Local $iSecs  = Int(Mod($iDiff, 60))
    GUICtrlSetData($hGUI_Runtime, StringFormat("Работает: %02d:%02d:%02d", $iHours, $iMins, $iSecs))

    ; 2. Обновление целевого окна из глобальной переменной (через Eval для внешней совместимости)
    If IsDeclared("ClientName") Then
        GUICtrlSetData($hGUI_Target, "Цель: " & Eval("ClientName"))
    EndIf

EndFunc   ;==>_GUI_Update


; Модифицируем вашу функцию _Log для поддержки вывода 5 строк в GUI
Func _GUI_AddLog($sNewText)
    Local $aLines = StringSplit($sLogBuffer, @CRLF, 1 + 2) ; 1=без разделителей, 2=массив без 0-индекса
    
    ; Добавляем новую строку в начало
    $sLogBuffer = $sNewText & @CRLF & $sLogBuffer
    
    ; Обрезаем до 5 строк
    Local $aCurrentLines = StringSplit($sLogBuffer, @CRLF, 1)
    $sLogBuffer = ""
    Local $iMax = $aCurrentLines[0] > 5 ? 5 : $aCurrentLines[0]
    For $i = 1 To $iMax
        $sLogBuffer &= $aCurrentLines[$i] & @CRLF
    Next
    
    If IsDeclared("hGUI_Log") Then GUICtrlSetData($hGUI_Log, $sLogBuffer)
EndFunc

; #FUNCTION# ====================================================================================================================
; Name...........: _Terminate
; Description....: Корректно завершает работу скрипта, очищая временные ресурсы и удаляя следы в системе.
; Syntax.........: _Terminate()
; Parameters ....: Нет
; Return values .: Нет (завершает процесс через Exit)
; Updated .......: 2026.04.25
; Version .......: 1.01
; Remarks .......: Удаляет папку ресурсов и выгружает временный шрифт. Безопасно работает через Eval.
; ===============================================================================================================================
Func _Terminate()
    
    _Log("!!! Скрипт остановлен пользователем !!!")
    
    ; 1. Получаем путь к ресурсам через Eval, чтобы избежать ошибки "undefined variable"
    Local $sResPath = ""
    If IsDeclared("sResourceDir") Then 
        $sResPath = Eval("sResourceDir")
    EndIf

    ; 2. Выгружаем временный шрифт из памяти системы
    If $sResPath <> "" Then
        DllCall("gdi32.dll", "int", "RemoveFontResourceEx", "str", $sResPath & "JetBrainsMono-Bold.ttf", "dword", 0x10, "int", 0)
    EndIf

    ; 3. Если была создана временная папка — удаляем её со всем содержимым
    If $sResPath <> "" Then 
        DirRemove($sResPath, 1)
    EndIf
    
    ; Даем небольшую паузу для записи последнего лога перед закрытием
    Sleep(500)
    
    Exit ; Полный выход из программы
EndFunc   ;==>_Terminate
