// CRC-32 IEEE 802.3 (polynôme réfléchi 16#EDB88320, init et xor final 16#FFFFFFFF), calculé par tranches pour
// ne pas dépasser le temps de cycle (docs/06 § 1). Même résultat que System.IO.Hashing.Crc32 côté HMI.
FUNCTION_BLOCK FB_Crc32
VAR
    _aTable : ARRAY[0..255] OF DWORD;
    _bTable : BOOL;
    _pData : POINTER TO BYTE;
    _nSize : UDINT;
    _nDone : UDINT;
    _nCrc : DWORD;
END_VAR

METHOD Begin
VAR_INPUT
    pData : POINTER TO BYTE;
    nSize : UDINT;
END_VAR
VAR
    i : UINT;
    k : USINT;
    c : DWORD;
END_VAR
IF NOT _bTable THEN
    FOR i := 0 TO 255 DO
        c := UINT_TO_DWORD(i);
        FOR k := 1 TO 8 DO
            IF (c AND 1) = 1 THEN c := SHR(c, 1) XOR 16#EDB88320; ELSE c := SHR(c, 1); END_IF
        END_FOR
        _aTable[i] := c;
    END_FOR
    _bTable := TRUE;
END_IF
_pData := pData;
_nSize := nSize;
_nDone := 0;
_nCrc := 16#FFFFFFFF;
END_METHOD

// Traite au plus nBudget octets ; TRUE quand tout est calculé
METHOD Step : BOOL
VAR_INPUT
    nBudget : UDINT;
END_VAR
VAR
    nEnd : UDINT;
    p : POINTER TO BYTE;
END_VAR
nEnd := MIN(_nSize, _nDone + nBudget);
p := _pData + _nDone;
WHILE _nDone < nEnd DO
    _nCrc := _aTable[DWORD_TO_USINT((_nCrc XOR BYTE_TO_DWORD(p^)) AND 16#FF)] XOR SHR(_nCrc, 8);
    p := p + 1;
    _nDone := _nDone + 1;
END_WHILE
Step := _nDone >= _nSize;
END_METHOD

PROPERTY Value : UDINT
GET
Value := DWORD_TO_UDINT(_nCrc XOR 16#FFFFFFFF);
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
