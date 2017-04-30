drop table if exists entries;
create table entries (
  id integer primary key autoincrement,
  ttime text not null,
  temp real not null
);