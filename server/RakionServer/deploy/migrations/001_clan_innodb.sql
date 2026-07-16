ALTER TABLE claninfo ENGINE=InnoDB;
ALTER TABLE clanrankp ENGINE=InnoDB;
ALTER TABLE clanschedule ENGINE=InnoDB;

ALTER TABLE claninfo
    ADD UNIQUE INDEX IF NOT EXISTS ux_claninfo_name (name);
ALTER TABLE usergameinfo
    ADD INDEX IF NOT EXISTS ix_usergameinfo_clanid (clanid),
    ADD INDEX IF NOT EXISTS ix_usergameinfo_treeuppername (treeuppername);
ALTER TABLE clanrankp
    ADD INDEX IF NOT EXISTS ix_clanrankp_clanid (clanid);
