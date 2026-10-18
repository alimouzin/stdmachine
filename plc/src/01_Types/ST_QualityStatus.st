// État de la surveillance des rebuts (docs/10 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_QualityStatus :
STRUCT
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_QualityTrack;
    aStationImputed : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_QualityTrack;
    aGroup : ARRAY[1..GVL_Const.MAX_GROUPS] OF ST_QualityTrack;
    aMover : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_QualityTrack;
    stMachine : ST_QualityTrack;
    bLoadingLocked : BOOL;
END_STRUCT
END_TYPE
