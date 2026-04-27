; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: EVE Echoes Bots                                                                                            #
; # VERSION........: 1.2.0                                                                                                      #
; # BUILD..........: 2026.04.27                                                                                                 #
; # FILENAME.......: ScreenshotMaker.au3                                                                                        #
; # GITHUB.........: https://github.com                                                          #
; # DESCRIPTION....: Автоматизированный комплекс управления добычей руды.                                                       #
; #                  - Захват экрана через ADB (MEmu)                                                                           #
; #                  - Конвертация в 24-bit BMP через GDIPlus (без внешних зависимостей)                                        #
; #                                                                                                                             #
; ###############################################################################################################################

#include <Constants.au3>
#include <libUtility.au3>
#include <GDIPlus.au3>

; --- НАСТРОЙКИ ---
Global $adbPath   = "C:\Program Files\Microvirt\MEmu\adb.exe"
Global $adbDevice = "127.0.0.1:21503"
Global $tempPng   = @ScriptDir & "\screenshots\temp_screen.png"
Global $finalBmp  = @ScriptDir & "\screenshots\game_screen.bmp"
; -----------------

; 1. ПРОВЕРКА НАЛИЧИЯ ADB
If Not FileExists($adbPath) Then
    MsgBox(16, "Ошибка", "Файл adb.exe не найден!")
    Exit
EndIf

; 2. ПОДГОТОВКА ДИРЕКТОРИИ
Local $dir = StringLeft($finalBmp, StringInStr($finalBmp, "\", 0, -1))
If Not FileExists($dir) Then DirCreate($dir)

; 3. ПОДКЛЮЧЕНИЕ
_CW("--> Подключение к " & $adbDevice & @CRLF)
RunWait('"' & $adbPath & '" connect ' & $adbDevice, "", @SW_HIDE)

; 4. ЗАХВАТ (PNG)
_CW("--> Снятие скриншота через ADB..." & @CRLF)
RunWait('"' & $adbPath & '" -s ' & $adbDevice & ' shell screencap -p /sdcard/screen.png', "", @SW_HIDE)
RunWait('"' & $adbPath & '" -s ' & $adbDevice & ' pull /sdcard/screen.png "' & $tempPng & '"', "", @SW_HIDE)

; 5. КОНВЕРТАЦИЯ В 24-BIT BMP (GDIPlus)
If FileExists($tempPng) Then
    _CW("--> Конвертация в 24-bit BMP (GDIPlus)..." & @CRLF)
    
    _GDIPlus_Startup()
    Local $hImage = _GDIPlus_ImageLoadFromFile($tempPng)
    
    ; Сохраняем как BMP. GDI+ по умолчанию делает совместимый формат.
    _GDIPlus_ImageSaveToFile($hImage, $finalBmp)
    
    ; Очистка ресурсов
    _GDIPlus_ImageDispose($hImage)
    _GDIPlus_Shutdown()
    
    FileDelete($tempPng)
Else
    _CW("!!! ОШИБКА: PNG файл не найден после захвата." & @CRLF)
    Exit
EndIf

; 6. ВЕРИФИКАЦИЯ
If FileExists($finalBmp) Then
    _CW("+++ УСПЕХ: " & $finalBmp & @CRLF)
Else
    _CW("!!! ОШИБКА КОНВЕРТАЦИИ" & @CRLF)
    MsgBox(16, "Ошибка", "Не удалось создать BMP файл.")
EndIf
