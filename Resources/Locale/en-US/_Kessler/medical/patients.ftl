## Living NPC patients

kessler-patient-cured = {CAPITALIZE(THE($patient))} lets out a long breath. The worst is over.
kessler-patient-discharged = {CAPITALIZE(THE($patient))} thanks the staff and heads off to catch the next shuttle.
kessler-patient-examine-cured = [color=green]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BASIC($patient, "look", "looks")} like {SUBJECT($patient)} {CONJUGATE-BE($patient)} on the mend.[/color]
kessler-patient-discharge-not-cured = The patient's condition hasn't been treated yet.
kessler-patient-redeem-label-not-cured = Patient not yet treated.
kessler-reputation-reason-patient = treated a patient

cmd-spawnpatient-desc = Spawns a living NPC patient at your feet, with a random or given condition.
cmd-spawnpatient-help = Usage: spawnpatient [condition]
cmd-spawnpatient-unknown = Unknown condition: {$condition}
cmd-spawnpatient-done = Spawned {$patient} with {$condition}.

## Poisoning

kessler-condition-poisoning = Poisoning
kessler-condition-poisoning-examine-1 = [color=yellow]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BASIC($patient, "look", "looks")} queasy and clammy.[/color]
kessler-condition-poisoning-examine-2 = [color=orange]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} grey and trembling, with a sour smell on {POSS-ADJ($patient)} breath.[/color]
kessler-condition-poisoning-examine-3 = [color=red]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} convulsing, {POSS-ADJ($patient)} lips blue.[/color]
kessler-condition-poisoning-symptom-nausea = {CAPITALIZE(THE($patient))} gags and clutches {POSS-ADJ($patient)} stomach.
kessler-condition-poisoning-symptom-sweat = {CAPITALIZE(THE($patient))} wipes cold sweat from {POSS-ADJ($patient)} face.
kessler-condition-poisoning-symptom-shake = {CAPITALIZE(THE($patient))} shakes uncontrollably.
kessler-condition-poisoning-symptom-collapse = {CAPITALIZE(THE($patient))} sags, barely able to stand.

## Internal bleeding

kessler-condition-internal-bleeding = Internal bleeding
kessler-condition-internal-bleeding-examine-1 = [color=yellow]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} pale and {CONJUGATE-BASIC($patient, "keep", "keeps")} a hand pressed to {POSS-ADJ($patient)} belly.[/color]
kessler-condition-internal-bleeding-examine-2 = [color=orange]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} ashen, and {POSS-ADJ($patient)} belly is swollen and tight.[/color]
kessler-condition-internal-bleeding-examine-3 = [color=red]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} white as a sheet and struggling to stay conscious.[/color]
kessler-condition-internal-bleeding-symptom-belly = {CAPITALIZE(THE($patient))} winces and doubles over.
kessler-condition-internal-bleeding-symptom-dizzy = {CAPITALIZE(THE($patient))} sways, dizzy.
kessler-condition-internal-bleeding-symptom-collapse = {CAPITALIZE(THE($patient))} gasps for breath, skin cold to the touch.

## Liver failure

kessler-condition-liver-failure = Liver failure
kessler-condition-liver-failure-examine-1 = [color=yellow]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BASIC($patient, "look", "looks")} tired, and the whites of {POSS-ADJ($patient)} eyes are faintly yellow.[/color]
kessler-condition-liver-failure-examine-2 = [color=orange]{CAPITALIZE(POSS-ADJ($patient))} skin and eyes have turned a sickly yellow.[/color]
kessler-condition-liver-failure-examine-3 = [color=red]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} deep yellow, confused and barely responsive.[/color]
kessler-condition-liver-failure-symptom-tired = {CAPITALIZE(THE($patient))} yawns and rubs {POSS-ADJ($patient)} eyes.
kessler-condition-liver-failure-symptom-itch = {CAPITALIZE(THE($patient))} scratches at {POSS-ADJ($patient)} arms.
kessler-condition-liver-failure-symptom-confused = {CAPITALIZE(THE($patient))} mumbles something that makes no sense.
kessler-condition-liver-failure-symptom-collapse = {CAPITALIZE(THE($patient))} slumps, eyes unfocused.

## Patient toxins

reagent-name-patient-cyanide = cyanide salts
reagent-desc-patient-cyanide = Stops cells from using oxygen. Treated with Dexalin Plus.
reagent-name-patient-thallium = thallium sulfate
reagent-desc-patient-thallium = A heavy metal poison once used in rat bait. Treated with charcoal.
reagent-name-patient-allergen = spore allergen
reagent-desc-patient-allergen = Fungal spores that set off a severe allergic reaction. Treated with diphenhydramine.
reagent-name-patient-polonium = polonium dust
reagent-desc-patient-polonium = A radioactive poison that wrecks the body from inside. Treated with arithrazine.
guide-entry-kessler-patients = Living Patients

## Distress beacons

kessler-distress-beacon-name = Distress beacon {$code}
kessler-distress-announcement = Distress call received from {$name} at ({$x}, {$y}). One survivor in a stasis pod. A rescue bonus is paid on discharge.

## Foreign body

kessler-condition-foreign-body = Foreign body
kessler-condition-foreign-body-examine-1 = [color=yellow]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BASIC($patient, "hold", "holds")} {POSS-ADJ($patient)} side stiffly, wincing at every move.[/color]
kessler-condition-foreign-body-examine-2 = [color=orange]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} hunched over, breathing in short, careful gasps.[/color]
kessler-condition-foreign-body-examine-3 = [color=red]{CAPITALIZE(SUBJECT($patient))} {CONJUGATE-BE($patient)} grey with pain, coughing up flecks of blood.[/color]
kessler-condition-foreign-body-symptom-wince = {CAPITALIZE(THE($patient))} winces and clutches {POSS-ADJ($patient)} chest.
kessler-condition-foreign-body-symptom-breath = {CAPITALIZE(THE($patient))} takes a sharp breath, as if something is stabbing {OBJECT($patient)}.
kessler-condition-foreign-body-symptom-cough = {CAPITALIZE(THE($patient))} coughs painfully.
kessler-condition-foreign-body-symptom-collapse = {CAPITALIZE(THE($patient))} doubles over, unable to straighten up.
