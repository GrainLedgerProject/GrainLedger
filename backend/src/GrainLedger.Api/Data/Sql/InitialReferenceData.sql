WITH initialized AS (
    INSERT INTO grainledger.ledger_receiving_threshold_history(revision,scope,action,new_percentage,reason,migration_id)
    VALUES(gen_random_uuid(),'SYSTEM_DEFAULT','INITIALIZE',2,'Initialize receiving transport-loss default','20261019000000_ReceivingLossThreshold')
    RETURNING revision,new_percentage
)
INSERT INTO grainledger.ledger_receiving_threshold_default(singleton,percentage,revision)
SELECT true,new_percentage,revision FROM initialized;
