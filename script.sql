CREATE TABLE tb_sla_registro (
                                 id_sla               NUMBER NOT NULL,
                                 cd_ocorrencia        VARCHAR2(40) NOT NULL,
                                 nm_categoria         VARCHAR2(60) NOT NULL,
                                 nr_prazo_horas       NUMBER(5) NOT NULL,
                                 dt_abertura          TIMESTAMP WITH TIME ZONE NOT NULL,
                                 dt_limite            TIMESTAMP WITH TIME ZONE NOT NULL,
                                 nm_executor          VARCHAR2(80) NOT NULL,
                                 ds_status            VARCHAR2(30) NOT NULL
);

ALTER TABLE tb_sla_registro ADD CONSTRAINT tb_sla_registro_pk PRIMARY KEY (id_sla);

CREATE SEQUENCE sq_tb_sla_registro START WITH 1 INCREMENT BY 1 NOCACHE;