// Valeurs rémanentes lues par la HMI (docs/02 § 7.4)
{attribute 'pack_mode' := '1'}
TYPE ST_RetainedValues :
STRUCT
    nRetainId : ULINT;
    nCount : UINT;
    aItem : ARRAY[1..2048] OF ST_RetainedItem;
END_STRUCT
END_TYPE
