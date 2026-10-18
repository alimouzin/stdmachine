// Programme de test TcUnit (docs/04 § 14) : à appeler dans la tâche du projet de test, qui référence
// StdMachineLib et TcUnit. Chaque suite déclarée ici est exécutée par TcUnit.RUN().
PROGRAM PRG_StdMachineTests
VAR
    fbPackML : FB_PackMLTest;
    fbHelpers : FB_HelpersTest;
    fbAlarms : FB_AlarmManagerTest;
    fbObjects : FB_ObjectsSimTest;
    fbScenario : FB_ScenarioEntryExitTest;
END_VAR

TcUnit.RUN();
END_PROGRAM
