; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: EVE Echoes Bot                                                                                             #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.27                                                                                                 #
; # FILENAME.......: ScreenshotMaker.au3                                                                                        #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Наборо инструментов для автоматизации рутины в EVE Echoes                                                  #
; #                  -                                                                                                          #
; #                  -                                                                                                          #
; #                  -                                                                                                          #
; #                                                                                                                             #
; ###############################################################################################################################

#include <Constants.au3>
#include <libUtility.au3>

; --- НАСТРОЙКИ ---
; Путь к исполняемому файлу ADB в директории MEmu
Global $adbPath = "C:\Program Files\Microvirt\MEmu\adb.exe"
; Адрес и порт эмулятора (по умолчанию для первого окна MEmu - 21503)
Global $adbDevice = "127.0.0.1:21503"
; Директория сохранения скриншотов
Global $savePath = @ScriptDir & "\screenshots\game_screen.png"
; -----------------

; 1. ПРОВЕРКА НАЛИЧИЯ ADB
If Not FileExists($adbPath) Then
    MsgBox(16, "Ошибка", "Файл adb.exe не найден по пути: " & @CRLF & $adbPath)
    Exit
EndIf

; 2. ПОДГОТОВКА ДИРЕКТОРИИ
Local $dir = StringLeft($savePath, StringInStr($savePath, "\", 0, -1))
If Not FileExists($dir) Then DirCreate($dir)

; 3. ПОДКЛЮЧЕНИЕ К УСТРОЙСТВУ
_CW("--> Подключение к эмулятору: " & $adbDevice & @CRLF)
RunWait('"' & $adbPath & '" connect ' & $adbDevice, "", @SW_HIDE)
Sleep(500) ; Пауза для стабилизации соединения

; 4. ЗАХВАТ ЭКРАНА
_CW("--> Создание скриншота через ADB..." & @CRLF)
; Снимаем скриншот во внутреннюю память Android
RunWait('"' & $adbPath & '" -s ' & $adbDevice & ' shell screencap -p /sdcard/screen.png', "", @SW_HIDE)
; Копируем файл из Android на локальный диск ПК
Local $res = RunWait('"' & $adbPath & '" -s ' & $adbDevice & ' pull /sdcard/screen.png "' & $savePath & '"', "", @SW_HIDE)

; 5. ВЕРИФИКАЦИЯ РЕЗУЛЬТАТА
If FileExists($savePath) Then
    _CW("+++ УСПЕХ! Снимок сохранен: " & $savePath & @CRLF)
Else
    _CW("!!! ОШИБКА: Не удалось получить скриншот." & @CRLF)
    MsgBox(16, "Ошибка", "Скриншот не получен. Убедитесь, что MEmu запущен и ADB-порт " & $adbDevice & " активен.")
EndIf
