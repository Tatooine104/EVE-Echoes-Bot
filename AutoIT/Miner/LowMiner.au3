#include <ImageSearch.au3>

Opt("MouseCoordMode", 2)
Opt("PixelCoordMode", 2)

Global $hMEmu = 0 ; Глобальная переменная для хранения ссылки на окно

; --- ПРИМЕР ИСПОЛЬЗОВАНИЯ ---

If _CheckAndActivateMEmu("(Memu W.01)") Then
    ; Если функция вернула True, работаем дальше
    Local $aPos = WinGetPos($hMEmu)

    Local $absX1 = $aPos[0] + 1060
    Local $absY1 = $aPos[1] + 230
    Local $absX2 = $aPos[0] + 1280
    Local $absY2 = $aPos[1] + 300

    Local $x, $y
    If _ImageSearchArea("UnDock.bmp", 1, $absX1, $absY1, $absX2, $absY2, $x, $y, 100) = 1 Then
        ;MouseClick("left", $x - $aPos[0], $y - $aPos[1], 1, 0)
		_CW("Всё отлично!" & @CRLF)
    Else
        _CW("Кнопка не найдена" & @CRLF)
    EndIf
Else
    MsgBox(16, "Внимание", "Эмулятор не готов к работе!")
EndIf

; --- САМА ФУНКЦИЯ ---

Func _CheckAndActivateMEmu($title)
    $hMEmu = WinGetHandle($title) ; Записываем в глобальную переменную

    If @error Then Return False ; Окно не существует

    WinActivate($hMEmu)

    ; Ждем активации окна 3 секунды
    If WinWaitActive($hMEmu, "", 1) Then
        Return True ; Все ок
    Else
        Return False ; Окно есть, но не смогло стать активным
    EndIf
EndFunc

Func _CW($sText)
    ConsoleWrite(BinaryToString(StringToBinary($sText & @CRLF, 4), 1))
EndFunc