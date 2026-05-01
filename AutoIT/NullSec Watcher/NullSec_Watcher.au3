; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Watcher                                                                                            #
; # VERSION........: 1.0.1 (Stable)                                                                                             #
; # DESCRIPTION....: Автономный модуль мониторинга локального чата и оповещения в альянс.                                       #
; #                                                                                                                             #
; ###############################################################################################################################

#AutoIt3Wrapper_UseX64=n ; Принудительный запуск в 32-битном режиме

#include <WinAPIFiles.au3>
#include <WinAPISys.au3>

#AutoIt3Wrapper_UseX64=n

; --- [ВАЖНО] СНАЧАЛА ОБЪЯВЛЯЕМ ГЛОБАЛЬНЫЕ ПЕРЕМЕННЫЕ ---
Global $iClientCount = 2
Global $aIsSave[$iClientCount]
Global $aIntelSent[$iClientCount]
Global $aDeviceIDs[$iClientCount] = ["127.0.0.1:21503", "127.0.0.1:21513"]

; 1. ИНИЦИАЛИЗАЦИЯ
; Используем PathRelativePathTo или просто нормализуем путь через WinAPI
#include <WinAPIFiles.au3>

; Создаем чистый путь без всяких ".."
Global $sResourceDir = FileGetShortName(_WinAPI_GetFullPathName(@ScriptDir & "\..\Images")) & "\"
Global $sImagesDir = $sResourceDir ; если вы используете обе переменные
Global $sDllDir = FileGetShortName("C:\Users\Tatooine\GitHub\EVE Echoes Bot\AutoIT\Resource\") & "\"

; Убираем возможные двойные слеши, которые ломают проверку
$sResourceDir = StringReplace($sResourceDir, "\\", "\")

; ПРОВЕРКА: Ищем конкретный файл, который точно должен быть в папке
If Not FileExists($sResourceDir & "imgLocalStatCriminal.bmp") Then
    MsgBox(16, "Критическая ошибка", "Бот ищет ресурсы здесь:" & @CRLF & $sResourceDir & @CRLF & @CRLF & "Но папка пуста или файла imgAllianceChat.bmp там нет.")
    Exit
EndIf

Global $Debug = True

; Заполняем начальные значения
For $i = 0 To $iClientCount - 1
    $aIsSave[$i] = True
    $aIntelSent[$i] = False
Next

; --- [ВАЖНО] ТОЛЬКО ТЕПЕРЬ ПОДКЛЮЧАЕМ БИБЛИОТЕКИ ---
; Теперь функции внутри них "увидят" уже созданные массивы
#include "..\Resource\libUtility.au3"
#include "..\Resource\libImageSearch.au3"
#include "..\Resource\libGUI.au3"
#include "..\Resource\libEVEEchoesFuncs.au3"

; 2. ЗАПУСК ИНТЕРФЕЙСА
_GUI_Init("Watcher Mode ADB", 10, 10)
_Log("Watcher запущен (ADB Mode). Ожидание нажатия СТАРТ...")

; 3. ОСНОВНОЙ ЦИКЛ
While 1
    _GUI_Update()

    If $bIsRunning Then
        For $i = 0 To $iClientCount - 1
            Local $sID = $aDeviceIDs[$i]

            ; ШАГ 1: Проверка безопасности
            If _IsSafe($sID, $i) Then
                _Log("!!! [" & $sID & "] ОБНАРУЖЕН ВРАГ!")

                ; ШАГ 2: Отправка доклада (флаг $aIntelSent проверяется внутри функции)
                ; _AlliChatMessage($sID, $i)

            Else
                ; Сброс флага отправки, если система стала безопасной
                If $aIntelSent[$i] = True Then
                    _Log("--> [" & $sID & "] Снова безопасно. Флаг разведки сброшен.")
                    $aIntelSent[$i] = False
                EndIf
            EndIf

            _HumanSleep(500, 1000)
        Next

        _HumanSleep(3000, 5000)
    Else
        Sleep(200)
    EndIf
WEnd
