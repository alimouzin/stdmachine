// Généré par tools/plc/gen_catalog.py depuis catalog/catalog.json : ne pas modifier à la main.
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    CatalogVersion : UINT := 1;

    // Erreurs communes à tous les objets (docs/03 § 1.3)
    Err_Timeout : UINT := 1;   // Timeout de l'action
    Err_Interlocked : UINT := 2;   // Verrouillage actif
    Err_NotReady : UINT := 3;   // Objet non prêt
    Err_UnknownAction : UINT := 4;   // Action inconnue pour ce type
    Err_BadParam : UINT := 5;   // Paramètre d'action invalide
    Err_Aborted : UINT := 6;   // Action interrompue par Abort
    Err_Disabled : UINT := 7;   // Objet désactivé
    Err_CommLost : UINT := 8;   // Communication perdue
    Err_NotInitialized : UINT := 9;   // Objet non initialisé
    Err_NotWired : UINT := 10;   // Matériel non raccordé
    Err_ManualOnly : UINT := 11;   // Action réservée au mode manuel

    // Types d'objets et de modules
    Type_DigitalInput : UINT := 1;   // Entrée numérique
    Type_DigitalOutput : UINT := 2;   // Sortie numérique
    Type_AnalogInput : UINT := 3;   // Entrée analogique
    Type_AnalogOutput : UINT := 4;   // Sortie analogique
    Type_Cylinder : UINT := 10;   // Vérin
    Type_LinearAxis : UINT := 11;   // Axe linéaire
    Type_RotaryAxis : UINT := 12;   // Axe rotatif
    Type_PlanarSystem : UINT := 13;   // Système planaire
    Type_PlanarMover : UINT := 14;   // Mover
    Type_ExternalRobot : UINT := 15;   // Robot externe
    Type_VacuumBlow : UINT := 20;   // Aspiration et soufflage
    Type_Gripper : UINT := 21;   // Pince
    Type_KeyenceIV : UINT := 30;   // Vision Keyence IV
    Type_KeyenceGT2 : UINT := 31;   // Palpeur Keyence GT2
    Type_AsyrilEyePlus : UINT := 32;   // Vision Asyril Eye+
    Type_Scale : UINT := 33;   // Pesage
    Type_CodeReader : UINT := 34;   // Lecteur de code
    Type_Light : UINT := 35;   // Éclairage
    Type_Press : UINT := 40;   // Presse
    Type_Screwdriver : UINT := 41;   // Visseuse
    Type_Dispenser : UINT := 42;   // Distributeur
    Type_Marker : UINT := 43;   // Marquage
    Type_VibratoryFeeder : UINT := 44;   // Chargeur vibrant
    Type_Magazine : UINT := 45;   // Palettiseur ou magasin
    Type_Beacon : UINT := 50;   // Balise
    Type_Safety : UINT := 51;   // Sécurités
    Type_Timer : UINT := 52;   // Temporisation
    Type_Counter : UINT := 53;   // Compteur
    Type_Group : UINT := 100;   // Groupe
    Type_CartesianRobot : UINT := 101;   // Robot cartésien XYZC

    // Entrée numérique (type 1) : actions et erreurs
    DigitalInput_Lire : UINT := 1;
    DigitalInput_AttendreActif : UINT := 2;
    DigitalInput_AttendreInactif : UINT := 3;

    // Sortie numérique (type 2) : actions et erreurs
    DigitalOutput_Activer : UINT := 1;
    DigitalOutput_Desactiver : UINT := 2;
    DigitalOutput_Impulsion : UINT := 3;

    // Entrée analogique (type 3) : actions et erreurs
    AnalogInput_Lire : UINT := 1;
    AnalogInput_AttendreSeuil : UINT := 2;
    AnalogInput_VerifierPlage : UINT := 3;
    AnalogInput_Err100 : UINT := 100;   // Valeur sous le seuil d'alarme bas
    AnalogInput_Err101 : UINT := 101;   // Valeur au-dessus du seuil d'alarme haut

    // Sortie analogique (type 4) : actions et erreurs
    AnalogOutput_Ecrire : UINT := 1;
    AnalogOutput_EcrireAvecRampe : UINT := 2;
    AnalogOutput_ValeurSure : UINT := 3;

    // Vérin (type 10) : actions et erreurs
    Cylinder_MoveTravail : UINT := 1;
    Cylinder_MoveRepos : UINT := 2;
    Cylinder_LirePosition : UINT := 3;
    Cylinder_Err100 : UINT := 100;   // Les deux capteurs actifs
    Cylinder_Err101 : UINT := 101;   // Perte du capteur de la position atteinte hors mouvement
    Cylinder_Err102 : UINT := 102;   // Capteur de départ toujours actif après 500 ms (vérin bloqué)

    // Axe linéaire (type 11) : actions et erreurs
    LinearAxis_PowerOn : UINT := 1;
    LinearAxis_PowerOff : UINT := 2;
    LinearAxis_Init : UINT := 3;
    LinearAxis_MoveAbsolu : UINT := 4;
    LinearAxis_MoveRelatif : UINT := 5;
    LinearAxis_Stop : UINT := 6;
    LinearAxis_AcquitterErreur : UINT := 7;
    LinearAxis_LirePosition : UINT := 8;
    LinearAxis_JogPlus : UINT := 9;
    LinearAxis_JogMoins : UINT := 10;
    LinearAxis_Err100 : UINT := 100;   // Axe non référencé
    LinearAxis_Err101 : UINT := 101;   // Cible hors course
    LinearAxis_Err102 : UINT := 102;   // Erreur du drive
    LinearAxis_Err103 : UINT := 103;   // Erreur de suivi
    LinearAxis_Err104 : UINT := 104;   // Perte d'alimentation
    LinearAxis_Err105 : UINT := 105;   // Axe non alimenté

    // Axe rotatif (type 12) : actions et erreurs
    RotaryAxis_PowerOn : UINT := 1;
    RotaryAxis_PowerOff : UINT := 2;
    RotaryAxis_Init : UINT := 3;
    RotaryAxis_MoveAbsolu : UINT := 4;
    RotaryAxis_MoveRelatif : UINT := 5;
    RotaryAxis_Stop : UINT := 6;
    RotaryAxis_AcquitterErreur : UINT := 7;
    RotaryAxis_LirePosition : UINT := 8;
    RotaryAxis_JogPlus : UINT := 9;
    RotaryAxis_JogMoins : UINT := 10;
    RotaryAxis_Err100 : UINT := 100;   // Axe non référencé
    RotaryAxis_Err101 : UINT := 101;   // Cible hors course
    RotaryAxis_Err102 : UINT := 102;   // Erreur du drive
    RotaryAxis_Err103 : UINT := 103;   // Erreur de suivi
    RotaryAxis_Err104 : UINT := 104;   // Perte d'alimentation
    RotaryAxis_Err105 : UINT := 105;   // Axe non alimenté

    // Système planaire (type 13) : actions et erreurs
    PlanarSystem_ActiverLeSysteme : UINT := 1;
    PlanarSystem_DesactiverLeSysteme : UINT := 2;
    PlanarSystem_AcquitterErreur : UINT := 3;
    PlanarSystem_Err100 : UINT := 100;   // Collision détectée
    PlanarSystem_Err101 : UINT := 101;   // Mover non détecté
    PlanarSystem_Err102 : UINT := 102;   // Erreur du groupe planaire

    // Mover (type 14) : actions et erreurs
    PlanarMover_MoveVersPosition : UINT := 1;
    PlanarMover_Stop : UINT := 2;
    PlanarMover_LirePosition : UINT := 3;

    // Robot externe (type 15) : actions et erreurs
    ExternalRobot_Init : UINT := 1;
    ExternalRobot_AllerA : UINT := 2;
    ExternalRobot_Prendre : UINT := 3;
    ExternalRobot_Deposer : UINT := 4;
    ExternalRobot_ExecuterProgramme : UINT := 5;
    ExternalRobot_Stop : UINT := 6;
    ExternalRobot_AcquitterErreur : UINT := 7;
    ExternalRobot_Err100 : UINT := 100;   // Robot en défaut
    ExternalRobot_Err101 : UINT := 101;   // Position hors de portée
    ExternalRobot_Err102 : UINT := 102;   // Zone interdite

    // Aspiration et soufflage (type 20) : actions et erreurs
    VacuumBlow_Aspirer : UINT := 1;
    VacuumBlow_Souffler : UINT := 2;
    VacuumBlow_Couper : UINT := 3;
    VacuumBlow_VerifierPresencePiece : UINT := 4;
    VacuumBlow_Err100 : UINT := 100;   // Vide non atteint (pièce absente ou fuite)
    VacuumBlow_Err101 : UINT := 101;   // Perte de vide pendant l'aspiration (pièce tombée)
    VacuumBlow_Err102 : UINT := 102;   // Seuils incohérents (P1 ≥ P2)

    // Pince (type 21) : actions et erreurs
    Gripper_Ouvrir : UINT := 1;
    Gripper_Fermer : UINT := 2;
    Gripper_SerrerSurPiece : UINT := 3;
    Gripper_VerifierPresencePiece : UINT := 4;
    Gripper_Err100 : UINT := 100;   // Pièce absente à la fermeture
    Gripper_Err101 : UINT := 101;   // Pièce perdue pendant le maintien
    Gripper_Err102 : UINT := 102;   // Capteurs incohérents

    // Vision Keyence IV (type 30) : actions et erreurs
    KeyenceIV_SelectionnerProgramme : UINT := 1;
    KeyenceIV_Declencher : UINT := 2;
    KeyenceIV_LireDernierResultat : UINT := 3;
    KeyenceIV_Err100 : UINT := 100;   // Capteur non prêt

    // Palpeur Keyence GT2 (type 31) : actions et erreurs
    KeyenceGT2_Mesurer : UINT := 1;
    KeyenceGT2_RemiseAZero : UINT := 2;
    KeyenceGT2_LireValeurCourante : UINT := 3;
    KeyenceGT2_Err100 : UINT := 100;   // Mesure hors plage
    KeyenceGT2_Err101 : UINT := 101;   // Capteur non prêt

    // Vision Asyril Eye+ (type 32) : actions et erreurs
    AsyrilEyePlus_Init : UINT := 1;
    AsyrilEyePlus_SelectionnerRecette : UINT := 2;
    AsyrilEyePlus_ChercherPiece : UINT := 3;
    AsyrilEyePlus_ObtenirPiece : UINT := 4;
    AsyrilEyePlus_Err100 : UINT := 100;   // Esclave EtherCAT hors ligne
    AsyrilEyePlus_Err101 : UINT := 101;   // Aucune pièce trouvée
    AsyrilEyePlus_Err102 : UINT := 102;   // Résultat invalide
    AsyrilEyePlus_Err103 : UINT := 103;   // Erreur Asyril

    // Pesage (type 33) : actions et erreurs
    Scale_Tarer : UINT := 1;
    Scale_Mesurer : UINT := 2;
    Scale_LireValeurCourante : UINT := 3;
    Scale_Err100 : UINT := 100;   // Mesure non stabilisée
    Scale_Err101 : UINT := 101;   // Surcharge

    // Lecteur de code (type 34) : actions et erreurs
    CodeReader_LireCode : UINT := 1;
    CodeReader_VerifierCode : UINT := 2;
    CodeReader_Err100 : UINT := 100;   // Aucun code lu
    CodeReader_Err101 : UINT := 101;   // Code non conforme au motif

    // Éclairage (type 35) : actions et erreurs
    Light_Allumer : UINT := 1;
    Light_Eteindre : UINT := 2;
    Light_ReglerIntensite : UINT := 3;
    Light_Flasher : UINT := 4;

    // Presse (type 40) : actions et erreurs
    Press_ExecuterRecette : UINT := 1;
    Press_RetourRepos : UINT := 2;
    Press_TarerLaForce : UINT := 3;
    Press_LireForceEtCourse : UINT := 4;
    Press_Err100 : UINT := 100;   // Force maximale dépassée (arrêt immédiat, défaut)
    Press_Err101 : UINT := 101;   // Recette interrompue
    Press_Err102 : UINT := 102;   // Capteur de force hors plage
    Press_Err103 : UINT := 103;   // Cible de la recette non atteinte
    Press_Err104 : UINT := 104;   // Recette inconnue

    // Visseuse (type 41) : actions et erreurs
    Screwdriver_SelectionnerProgramme : UINT := 1;
    Screwdriver_Visser : UINT := 2;
    Screwdriver_Devisser : UINT := 3;
    Screwdriver_LireDernierResultat : UINT := 4;
    Screwdriver_Err100 : UINT := 100;   // Couple maximal dépassé
    Screwdriver_Err101 : UINT := 101;   // Angle hors fenêtre
    Screwdriver_Err102 : UINT := 102;   // Vis non prise
    Screwdriver_Err103 : UINT := 103;   // Défaut de l'outil

    // Distributeur (type 42) : actions et erreurs
    Dispenser_Deposer : UINT := 1;
    Dispenser_Amorcer : UINT := 2;
    Dispenser_Purger : UINT := 3;
    Dispenser_LireNiveau : UINT := 4;
    Dispenser_Err100 : UINT := 100;   // Cartouche vide
    Dispenser_Err101 : UINT := 101;   // Pression hors plage
    Dispenser_Err102 : UINT := 102;   // Dépôt non détecté

    // Marquage (type 43) : actions et erreurs
    Marker_SelectionnerModele : UINT := 1;
    Marker_Marquer : UINT := 2;
    Marker_VerifierMarquage : UINT := 3;
    Marker_LireEtat : UINT := 4;
    Marker_Err100 : UINT := 100;   // Marqueur non prêt
    Marker_Err101 : UINT := 101;   // Capot ouvert
    Marker_Err102 : UINT := 102;   // Défaut de la source

    // Chargeur vibrant (type 44) : actions et erreurs
    VibratoryFeeder_Vibrer : UINT := 1;
    VibratoryFeeder_DistribuerPieces : UINT := 2;
    VibratoryFeeder_Vider : UINT := 3;
    VibratoryFeeder_LireNiveau : UINT := 4;
    VibratoryFeeder_Err100 : UINT := 100;   // Trémie vide (avertissement, balise bleu clignotant)
    VibratoryFeeder_Err101 : UINT := 101;   // Défaut du chargeur

    // Palettiseur ou magasin (type 45) : actions et erreurs
    Magazine_PresenterPlateauSuivant : UINT := 1;
    Magazine_EvacuerPlateau : UINT := 2;
    Magazine_PositionDeLaCase : UINT := 3;
    Magazine_MarquerCase : UINT := 4;
    Magazine_LireOccupation : UINT := 5;
    Magazine_Err100 : UINT := 100;   // Magasin vide
    Magazine_Err101 : UINT := 101;   // Magasin plein
    Magazine_Err102 : UINT := 102;   // Plateau absent
    Magazine_Err103 : UINT := 103;   // Plateau non évacué

    // Balise (type 50) : actions et erreurs
    Beacon_Tester : UINT := 1;
    Beacon_Buzzer : UINT := 2;

    // Sécurités (type 51) : actions et erreurs
    Safety_LireEtat : UINT := 1;
    Safety_Acquitter : UINT := 2;
    Safety_AttendreOK : UINT := 3;
    Safety_Err100 : UINT := 100;   // Circuit ouvert
    Safety_Err101 : UINT := 101;   // Incohérence de canaux
    Safety_Err102 : UINT := 102;   // Acquittement requis

    // Temporisation (type 52) : actions et erreurs
    Timer_Attendre : UINT := 1;

    // Compteur (type 53) : actions et erreurs
    Counter_Incrementer : UINT := 1;
    Counter_Lire : UINT := 2;
    Counter_RemettreAZero : UINT := 3;
    Counter_Comparer : UINT := 4;
    Counter_Err100 : UINT := 100;   // Valeur maximale dépassée

    // Tables par n° de type (bit n-1 = action n)
    aActionMask    : ARRAY[0..149] OF UDINT := [0, 4(7), 5(0), 7, 2(1023), 2(7), 127, 4(0), 2(15), 8(0), 2(7), 15, 7, 3, 15, 4(0), 5(15), 31, 4(0), 3, 7, 1, 15, 96(0)];
    aMotionMask    : ARRAY[0..149] OF UDINT := [10(0), 3, 2(796), 0, 1, 31, 24(0), 3, 109(0)];
    aManualOnlyMask: ARRAY[0..149] OF UDINT := [11(0), 2(768), 37(0), 3, 99(0)];
    aImmediateMask : ARRAY[0..149] OF UDINT := [0, 1, 3, 2(5), 5(0), 4, 2(896), 0, 4, 5(0), 12, 8, 8(0), 2(4), 0, 4, 0, 7, 4(0), 5(8), 28, 4(0), 3, 1, 0, 15, 96(0)];
    // Timeout par défaut du catalogue (ms), [type, action] ; 0 = calculé par l'objet ou sans timeout
    aDefaultTimeoutMs : ARRAY[0..149, 1..31] OF UDINT := [32(0), 2(5000), 30(0), 1000, 29(0), 5000, 30(0), 1000, 184(0), 2(2000), 29(0), 2(3000), 30000, 2(0), 2(3000), 24(0), 2(3000), 30000, 2(0), 2(3000), 24(0), 20000, 10000, 5000, 28(0), 30000, 3000, 29(0), 60000, 3(20000), 60000, 3000, 5000, 148(0), 1000, 500, 29(0), 3(1000), 276(0), 3000, 2000, 29(0), 2000, 1000, 29(0), 10000, 5000, 3000, 1000, 27(0), 3000, 5000, 29(0), 2(2000), 32(0), 200, 152(0), 5000, 2000, 28(0), 2000, 2(10000), 28(0), 5000, 10000, 1000, 28(0), 2000, 10000, 3000, 28(0), 2(1000), 10000, 28(0), 2(10000), 185(0), 1000, 30000, 3066(0)];
END_VAR
