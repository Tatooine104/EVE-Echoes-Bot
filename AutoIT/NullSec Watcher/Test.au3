#AutoIt3Wrapper_UseX64=n

#include "..\Resource\libUtility.au3"
#include <libImageSearch.au3>
#include <ScreenCapture.au3>

Opt("WinTitleMatchMode", 2)

Global $lDebug = False

$hWindow = _GetHandleByTitle("MEmu W.01")

Local $aImages = ["imgLocalStatCriminal.bmp", "imgLocalStatMinus.bmp", "imgLocalStatNeitral.bmp"]


_Log("=== Запуск перебора картинок ===", $lDebug)

; Получаем координаты окна для привязки области поиска
Local $aPos = WinGetPos($hWindow)

If IsArray($aPos) Then
    ; Рассчитываем твою область поиска (5, 350, 350, 720) относительно окна
    Local $X1 = $aPos[0] + 5    ; Лево
    Local $Y1 = $aPos[1] + 350  ; Верх
    Local $X2 = $aPos[0] + 300  ; Право
    Local $Y2 = $aPos[1] + 720  ; Низ

; ... внутри блока If IsArray($aPos) ...
_ScreenCapture_Capture(@ScriptDir & "\debug_area.bmp", $X1, $Y1, $X2, $Y2)
_Log("Сделан проверочный скриншот области поиска", $lDebug)

    ; Перебор массива
    For $i = 0 To UBound($aImages) - 1
        _Log("Ищу объект: " & $aImages[$i], $lDebug)

        ; Ищем картинку в заданном прямоугольнике
        $aResult = _FindImage($X1, $Y1, $X2, $Y2, $aImages[$i], 180, $lDebug)

        If IsArray($aResult) Then
            _Log("SUCCESS: [" & $aImages[$i] & "] найдена! Координаты: " & $aResult[0] & "x" & $aResult[1], $lDebug)

            ; Пример клика по найденной картинке (с небольшой рандомизацией, чтобы не спалиться)
            ; MouseClick("left", $aResult[0] + Random(-2, 2, 1), $aResult[1] + Random(-2, 2, 1), 1, 5)
        Else
            _Log("FAILED: [" & $aImages[$i] & "] не обнаружена.", $lDebug)
        EndIf

        Sleep(500) ; Пауза, чтобы MEmu успевал отрисовывать кадры
    Next
Else
    _Log("ОШИБКА: Не удалось получить координаты окна!", $lDebug)
EndIf

_Log("=== Перебор завершен ===", $lDebug)
